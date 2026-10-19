// The engine-independent part of the Lokutor game SDK: one voice conversation with one character
// over the Lokutor voice-agent WebSocket (wss://api.lokutor.com/ws/agent). No UnityEngine
// references, so it builds and is tested with plain .NET; LokutorAgent (Unity) is a thin layer on top.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    public enum AgentState
    {
        Disconnected,
        Connecting,
        /// <summary>Connected and waiting for the player.</summary>
        Ready,
        /// <summary>The player is speaking.</summary>
        PlayerTalking,
        /// <summary>The character is preparing an answer.</summary>
        Thinking,
        /// <summary>The character's voice is playing.</summary>
        Speaking,
    }

    public sealed class LokutorError
    {
        public string Code { get; }
        public string Message { get; }
        public LokutorError(string code, string message) { Code = code ?? "error"; Message = message ?? ""; }
        public override string ToString() => Code + ": " + Message;
    }

    /// <summary>A mouth shape, coarse enough to map onto any rig's blend shapes or sprites.</summary>
    public enum MouthShape { Rest, A, E, I, O, U, MBP, FV, L, S, Other }

    /// <summary>When a character of the reply is spoken, from the start of that reply's audio.</summary>
    public struct VisemeMark
    {
        public float Time;
        public char Char;
        public MouthShape Shape;

        public static MouthShape ShapeFor(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'a': case 'á': case 'à': case 'â': case 'ã': return MouthShape.A;
                case 'e': case 'é': case 'è': case 'ê': return MouthShape.E;
                case 'i': case 'í': case 'y': return MouthShape.I;
                case 'o': case 'ó': case 'ò': case 'ô': case 'õ': return MouthShape.O;
                case 'u': case 'ú': case 'ü': case 'w': return MouthShape.U;
                case 'm': case 'b': case 'p': return MouthShape.MBP;
                case 'f': case 'v': return MouthShape.FV;
                case 'l': case 't': case 'd': case 'n': return MouthShape.L;
                case 's': case 'z': case 'c': case 'x': case 'ç': return MouthShape.S;
                default: return char.IsLetter(c) ? MouthShape.Other : MouthShape.Rest;
            }
        }
    }

    public sealed class SessionOptions
    {
        /// <summary>Your Lokutor API key. Fine for prototypes; a shipped game should use TicketProvider.</summary>
        public string ApiKey;

        /// <summary>
        /// Returns a short-lived ticket minted by YOUR backend (POST https://api.lokutor.com/ws-ticket with
        /// the API key), so the key never ships inside the game. Called before every connect.
        /// </summary>
        public Func<Task<string>> TicketProvider;

        /// <summary>An agent built in the Lokutor dashboard. Persona, voice and language then default to it.</summary>
        public string AgentId;

        /// <summary>Who the character is and how it talks (the system prompt).</summary>
        public string Persona;

        /// <summary>"F1".."F5", "M1".."M5" (a vetted speaker in the call's language) or a voice id from GET /voices.</summary>
        public string Voice;

        /// <summary>"en", "es", "fr", "pt", "ca", "gl", "eu", ...</summary>
        public string Language;

        /// <summary>Also receive per-character timings for lip-sync (VisemesReceived).</summary>
        public bool Visemes;

        /// <summary>
        /// Send silence instead of the microphone while the character's voice is playing, so laptop
        /// speakers do not make it interrupt itself. Turn off with headphones to let players interrupt.
        /// </summary>
        public bool MuteMicWhileSpeaking = true;

        /// <summary>
        /// How long the microphone stays muted after the voice has finished playing: the room's echo
        /// still reaches it for a moment.
        /// </summary>
        public int EchoTailMs = 300;

        public string Url = "wss://api.lokutor.com/ws/agent";
        public string UserAgent = "lokutor-game-sdk/0.1";
        public TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How events reach your code. Null (the default): they are queued and delivered when you call
        /// <see cref="LokutorSession.Update"/>, i.e. on your game's main thread.
        /// </summary>
        public Action<Action> Dispatcher;
    }

    public sealed class LokutorSession : IDisposable
    {
        public const int DefaultPlaybackRate = 44100;
        public const int DefaultInputRate = 16000;

        readonly SessionOptions options;
        readonly Dictionary<string, AgentAction> actions = new Dictionary<string, AgentAction>();
        readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        readonly ConcurrentQueue<byte[]> binaryOut = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<string> textOut = new ConcurrentQueue<string>();
        readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0);

        ClientWebSocket ws;
        CancellationTokenSource cts;
        Task receiveTask, sendTask;
        StreamingResampler micResampler;
        int currentGeneration;
        DateTime lastAgentAudioUtc = DateTime.MinValue;
        DateTime speakingSinceUtc = DateTime.MinValue;
        volatile bool replyAudioArrived;
        long audibleUntilTicks;
        DateTime playerTalkingSinceUtc = DateTime.MinValue;
        // After an action, the character still owes the reply that uses its result: until that reply's
        // text arrives (or 8 s), it is thinking, not done.
        long replyOwedUntilTicks;
        long lastMicVoiceTicks;
        volatile AgentState state = AgentState.Disconnected;
        AgentState reportedState = AgentState.Disconnected;

        public LokutorSession(SessionOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            Playback = new PlaybackBuffer(DefaultPlaybackRate);
        }

        // ---- What the game reads -----------------------------------------------------------------

        /// <summary>The character's voice. Pull from it in your audio callback at <see cref="PlaybackRate"/>.</summary>
        public PlaybackBuffer Playback { get; }
        public int PlaybackRate => Playback.SampleRate;
        public int InputRate { get; private set; } = DefaultInputRate;
        public AgentState State => state;
        public bool IsConnected => ws != null && ws.State == WebSocketState.Open;
        public string SessionId { get; private set; }
        /// <summary>Loudness of the character's voice right now, 0..1: drive a mouth or jaw with it.</summary>
        public float MouthOpen => Playback.Level;

        // ---- Events (delivered on Update() unless a Dispatcher is set) -----------------------------

        public event Action<AgentState> StateChanged;
        /// <summary>What the player said, once the turn is over.</summary>
        public event Action<string> PlayerSaid;
        /// <summary>The player's words while they are still talking.</summary>
        public event Action<string> PlayerSaidPartial;
        /// <summary>What the character is about to say.</summary>
        public event Action<string> AgentSaid;
        /// <summary>The player cut the character off; this is what they actually heard.</summary>
        public event Action<string> AgentInterrupted;
        /// <summary>An action ran (after its handler returned): name, arguments, result.</summary>
        public event Action<string, ActionArgs, string> ActionCalled;
        public event Action<IReadOnlyList<VisemeMark>> VisemesReceived;
        public event Action<LokutorError> Error;
        /// <summary>The server announced how many seconds this session may still run.</summary>
        public event Action<int> SessionLimit;
        public event Action<string> Disconnected;

        // ---- Setup -----------------------------------------------------------------------------------

        /// <summary>
        /// Lets the character call a game function. Register before <see cref="ConnectAsync"/>; actions
        /// added later are sent when you call <see cref="RefreshActions"/>.
        /// </summary>
        public LokutorSession AddAction(AgentAction action)
        {
            lock (actions) actions[action.Name] = action;
            return this;
        }

        public LokutorSession AddAction(string name, string description, Func<ActionArgs, string> handler, params ActionParam[] parameters)
            => AddAction(new AgentAction(name, description, handler, parameters));

        public void RemoveAction(string name)
        {
            lock (actions) actions.Remove(name);
        }

        public async Task ConnectAsync(CancellationToken cancel = default)
        {
            if (ws != null) throw new InvalidOperationException("Already connected; call DisconnectAsync first.");
            if (string.IsNullOrEmpty(options.ApiKey) && options.TicketProvider == null)
                throw new InvalidOperationException("Set SessionOptions.ApiKey (or TicketProvider).");

            SetState(AgentState.Connecting);
            currentGeneration = 0;
            Playback.Clear();
            micResampler = null;

            var query = new List<string>();
            if (!string.IsNullOrEmpty(options.AgentId)) query.Add("agent_id=" + Uri.EscapeDataString(options.AgentId));
            var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("User-Agent", options.UserAgent);
            if (options.TicketProvider != null)
            {
                string ticket = await options.TicketProvider().ConfigureAwait(false);
                query.Add("api_key=" + Uri.EscapeDataString(ticket));
            }
            else
            {
                socket.Options.SetRequestHeader("X-API-Key", options.ApiKey);
            }
            var uri = new Uri(options.Url + (query.Count > 0 ? "?" + string.Join("&", query) : ""));

            cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                timeout.CancelAfter(options.ConnectTimeout);
                try
                {
                    await socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    socket.Dispose();
                    cts.Dispose();
                    cts = null;
                    SetState(AgentState.Disconnected);
                    throw new IOException("Could not reach Lokutor (" + e.Message + "). Check the API key and network.", e);
                }
            }
            ws = socket;
            receiveTask = Task.Run(() => ReceiveLoop(cts.Token));
            sendTask = Task.Run(() => SendLoop(cts.Token));
            SendConfig();
        }

        void SendConfig()
        {
            bool dashboardAgent = !string.IsNullOrEmpty(options.AgentId);
            // A dashboard agent keeps its own voice, language and prompt unless the game overrides them.
            if (!string.IsNullOrEmpty(options.Voice)) SendJson(Msg("voice", options.Voice));
            else if (!dashboardAgent) SendJson(Msg("voice", "F1"));
            if (!string.IsNullOrEmpty(options.Language)) SendJson(Msg("language", options.Language));
            else if (!dashboardAgent) SendJson(Msg("language", "en"));
            SendTools();
            SendJson(Msg("visemes", options.Visemes));
            if (!string.IsNullOrEmpty(options.Persona)) SendJson(Msg("prompt", options.Persona));
            else if (!dashboardAgent) SendJson(Msg("prompt", "You are a friendly character in a video game. Keep answers short."));
            // Last: the server starts the conversation once it has the whole configuration.
            SendJson(new Dictionary<string, object> { ["type"] = "rates", ["playback"] = DefaultPlaybackRate, ["input"] = DefaultInputRate });
        }

        void SendTools()
        {
            var defs = new List<object>();
            lock (actions)
                foreach (var a in actions.Values) defs.Add(a.ToToolDefinition());
            if (defs.Count > 0) SendJson(Msg("tools", defs));
        }

        /// <summary>Re-sends the action list (after AddAction/RemoveAction mid-conversation).</summary>
        public void RefreshActions() => SendTools();

        /// <summary>Changes who the character is, mid-conversation.</summary>
        public void SetPersona(string persona)
        {
            options.Persona = persona;
            if (IsConnected) SendJson(Msg("prompt", persona));
        }

        /// <summary>Forgets the conversation so far (the character starts fresh).</summary>
        public void ResetConversation()
        {
            if (IsConnected) SendJson(new Dictionary<string, object> { ["type"] = "reset" });
        }

        // ---- Microphone ----------------------------------------------------------------------------

        /// <summary>
        /// Sends microphone audio (mono floats, any sample rate) to the character. Call it with whatever
        /// the engine captured since last frame; resampling and framing are handled here. Keep calling
        /// it with silence (zeros) when the player is quiet: the server hears pauses as end of turn.
        /// </summary>
        public void SendMicrophone(float[] samples, int offset, int count, int sampleRate)
        {
            if (!IsConnected || count <= 0) return;
            if (micResampler == null || micResampler.InputRate != sampleRate || micResampler.OutputRate != InputRate)
                micResampler = new StreamingResampler(sampleRate, InputRate);
            var resampled = micResampler.Process(samples, offset, count);
            if (resampled.Length == 0) return;
            if (options.MuteMicWhileSpeaking && AgentAudible) Array.Clear(resampled, 0, resampled.Length);
            NoteMicLevel(resampled);
            binaryOut.Enqueue(Pcm16.FromFloat(resampled, 0, resampled.Length));
            sendSignal.Release();
        }

        /// <summary>Sends 16 kHz mono PCM16 as is (e.g. a recorded line).</summary>
        public void SendPcm16(byte[] pcm16At16k)
        {
            if (!IsConnected || pcm16At16k == null || pcm16At16k.Length == 0) return;
            NoteMicLevel(Pcm16.ToFloat(pcm16At16k));
            binaryOut.Enqueue(pcm16At16k);
            sendSignal.Release();
        }

        bool ReplyOwed => DateTime.UtcNow.Ticks < System.Threading.Interlocked.Read(ref replyOwedUntilTicks);

        void NoteMicLevel(float[] samples)
        {
            if (samples.Length == 0) return;
            double sum = 0;
            foreach (var v in samples) sum += v * v;
            if (Math.Sqrt(sum / samples.Length) > 0.02) System.Threading.Interlocked.Exchange(ref lastMicVoiceTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Tells the session when the voice the engine has already taken from <see cref="Playback"/> will
        /// have finished coming out of the speakers (its own buffers and output latency included), so
        /// the microphone stays muted until then. Engines that do not call it get EchoTailMs after their
        /// last read, which is enough when they read close to real time.
        /// </summary>
        public void SetVoiceAudibleUntil(DateTime utc) => System.Threading.Interlocked.Exchange(ref audibleUntilTicks, utc.Ticks);

        /// <summary>The character can still be heard, so it could reach the microphone: voice waiting,
        /// voice in the engine's buffers, or the room's echo just after.</summary>
        bool AgentAudible
        {
            get
            {
                if (Playback.IsPlaying) return true;
                long until = Math.Max(System.Threading.Interlocked.Read(ref audibleUntilTicks), Playback.LastVoiceReadUtc.Ticks);
                return DateTime.UtcNow.Ticks < until + TimeSpan.FromMilliseconds(options.EchoTailMs).Ticks;
            }
        }

        // ---- Main-thread pump --------------------------------------------------------------------

        /// <summary>
        /// Call once per frame from the game's main thread. Delivers queued events, runs action
        /// handlers, and moves the state from Speaking back to Ready once the voice has played out.
        /// </summary>
        public void Update()
        {
            // Speaking ends once this reply's audio has arrived, nothing new has come for a moment, and it
            // can no longer be heard (engine buffers and the room's echo included): Ready then means the
            // microphone is really open again. The server says "speaking" when synthesis starts, which
            // can be half a second before the first audio; if no audio ever comes, give up after 5 s.
            if (state == AgentState.Speaking && !Playback.IsPlaying)
            {
                var now = DateTime.UtcNow;
                bool finished = replyAudioArrived
                    ? (now - (lastAgentAudioUtc > speakingSinceUtc ? lastAgentAudioUtc : speakingSinceUtc)).TotalMilliseconds > 400 && !AgentAudible
                    : (now - speakingSinceUtc).TotalSeconds > 5;
                if (finished) SetState(ReplyOwed ? AgentState.Thinking : AgentState.Ready);
            }
            // The server says when the player starts talking, but not when a start led nowhere (a noise,
            // or speech it set aside). Without a reply under way, the player is done once the microphone
            // has been quiet for 2 s.
            if (state == AgentState.PlayerTalking)
            {
                var now = DateTime.UtcNow;
                var lastVoice = new DateTime(System.Threading.Interlocked.Read(ref lastMicVoiceTicks), DateTimeKind.Utc);
                if ((now - playerTalkingSinceUtc).TotalSeconds > 2.5 && (now - lastVoice).TotalSeconds > 2.0 && !ReplyOwed)
                    SetState(AgentState.Ready);
            }
            if (state == AgentState.Thinking && !ReplyOwed && System.Threading.Interlocked.Read(ref replyOwedUntilTicks) != 0)
            {
                // The reply owed after an action never came (8 s): stop waiting for it.
                System.Threading.Interlocked.Exchange(ref replyOwedUntilTicks, 0);
                SetState(AgentState.Ready);
            }
            while (pending.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { RaiseError(new LokutorError("callback", e.Message)); }
            }
        }

        void Post(Action a)
        {
            if (options.Dispatcher != null) options.Dispatcher(a);
            else pending.Enqueue(a);
        }

        void SetState(AgentState next)
        {
            if (next == AgentState.PlayerTalking && state != AgentState.PlayerTalking) playerTalkingSinceUtc = DateTime.UtcNow;
            if (next == AgentState.Speaking && state != AgentState.Speaking)
            {
                speakingSinceUtc = DateTime.UtcNow;
                replyAudioArrived = false;
            }
            state = next;
            Post(() =>
            {
                if (reportedState == next) return;
                reportedState = next;
                StateChanged?.Invoke(next);
            });
        }

        void RaiseError(LokutorError e) => Post(() => Error?.Invoke(e));

        // ---- Network -----------------------------------------------------------------------------

        static Dictionary<string, object> Msg(string type, object data) =>
            new Dictionary<string, object> { ["type"] = type, ["data"] = data };

        void SendJson(Dictionary<string, object> msg)
        {
            textOut.Enqueue(MiniJson.Serialize(msg));
            sendSignal.Release();
        }

        async Task SendLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await sendSignal.WaitAsync(token).ConfigureAwait(false);
                    var socket = ws;
                    if (socket == null || socket.State != WebSocketState.Open) continue;
                    // Text first: configuration and tool results must not wait behind audio.
                    if (textOut.TryDequeue(out var text))
                    {
                        var bytes = Encoding.UTF8.GetBytes(text);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                    }
                    else if (binaryOut.TryDequeue(out var audio))
                    {
                        await socket.SendAsync(new ArraySegment<byte>(audio), WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException e) { RaiseError(new LokutorError("connection", e.Message)); }
            catch (ObjectDisposedException) { }
        }

        async Task ReceiveLoop(CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            string reason = "closed";
            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        reason = string.IsNullOrEmpty(ws.CloseStatusDescription)
                            ? "closed (" + (int)(ws.CloseStatus ?? WebSocketCloseStatus.NormalClosure) + ")"
                            : ws.CloseStatusDescription;
                        break;
                    }
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var data = message.ToArray();
                    message.SetLength(0);
                    if (r.MessageType == WebSocketMessageType.Binary) OnAgentAudio(Pcm16.ToFloat(data), currentGeneration);
                    else HandleServerMessage(Encoding.UTF8.GetString(data));
                }
            }
            catch (OperationCanceledException) { reason = "disconnected"; }
            catch (WebSocketException e) { reason = "connection lost: " + e.Message; }
            catch (ObjectDisposedException) { reason = "disconnected"; }
            SetState(AgentState.Disconnected);
            Post(() => Disconnected?.Invoke(reason));
        }

        /// <summary>Takes the text frames waiting to be sent. For tests, which run without a socket.</summary>
        internal List<string> DrainOutgoingTextForTests()
        {
            var list = new List<string>();
            while (textOut.TryDequeue(out var t)) list.Add(t);
            return list;
        }

        /// <summary>Handles one JSON frame from the server. Internal so tests can feed it frames.</summary>
        internal void HandleServerMessage(string json)
        {
            Dictionary<string, object> msg;
            try { msg = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (FormatException) { return; }
            if (msg == null) return;
            string type = Str(msg, "type");
            switch (type)
            {
                case "audio":
                {
                    int gen = Int(msg, "generation");
                    string b64 = Str(msg, "data");
                    if (!string.IsNullOrEmpty(b64)) OnAgentAudio(Pcm16.ToFloat(Convert.FromBase64String(b64)), gen);
                    break;
                }
                case "server_rates":
                {
                    int playback = Int(msg, "playback"), input = Int(msg, "input");
                    if (input > 0) InputRate = input;
                    if (playback > 0 && playback != Playback.SampleRate)
                    {
                        Playback.Clear();
                        Playback.SampleRate = playback;
                    }
                    break;
                }
                case "status":
                    OnStatus(Str(msg, "data"), Int(msg, "generation"), Str(msg, "session_id"));
                    break;
                case "transcript":
                {
                    string text = Str(msg, "data");
                    if (string.IsNullOrEmpty(text)) text = Str(msg, "text");
                    bool partial = msg.TryGetValue("partial", out var p) && p is bool pb && pb;
                    if (Str(msg, "role") == "agent")
                    {
                        System.Threading.Interlocked.Exchange(ref replyOwedUntilTicks, 0); // the reply after an action has come
                        Post(() => AgentSaid?.Invoke(text));
                    }
                    else if (partial) Post(() => PlayerSaidPartial?.Invoke(text));
                    else Post(() => PlayerSaid?.Invoke(text));
                    break;
                }
                case "transcript_truncated":
                {
                    string heard = Str(msg, "data");
                    Post(() => AgentInterrupted?.Invoke(heard));
                    break;
                }
                case "tool_call":
                    OnToolCall(Str(msg, "name"), Str(msg, "arguments"), Str(msg, "call_id"));
                    break;
                case "visemes":
                    OnVisemes(msg.TryGetValue("data", out var vd) ? vd as List<object> : null);
                    break;
                case "session_limit":
                {
                    var d = msg.TryGetValue("data", out var sl) ? sl as Dictionary<string, object> : null;
                    int seconds = d != null ? Int(d, "seconds") : 0;
                    Post(() => SessionLimit?.Invoke(seconds));
                    break;
                }
                case "error":
                {
                    msg.TryGetValue("data", out var ed);
                    var e = ed is Dictionary<string, object> d
                        ? new LokutorError(Str(d, "code"), Str(d, "message"))
                        : new LokutorError("error", ed as string ?? "server error");
                    RaiseError(e);
                    break;
                }
            }
        }

        void OnStatus(string status, int generation, string sessionId)
        {
            switch (status)
            {
                case "connected":
                    if (!string.IsNullOrEmpty(sessionId)) SessionId = sessionId;
                    SetState(AgentState.Ready);
                    break;
                case "listening":
                    // The player started talking. Not an interruption yet: it may be a cough.
                    if (state != AgentState.Speaking) SetState(AgentState.PlayerTalking);
                    break;
                case "interrupted":
                    Playback.Clear();
                    SetState(AgentState.PlayerTalking);
                    break;
                case "thinking":
                    if (generation > currentGeneration)
                    {
                        // A new reply: whatever was still queued belongs to the old one.
                        currentGeneration = generation;
                        Playback.Clear();
                        SetState(AgentState.Thinking);
                    }
                    else if (state != AgentState.Speaking && !Playback.IsPlaying)
                    {
                        // The server says "thinking" again before each sentence of the same reply;
                        // only a character that is not already talking is thinking.
                        SetState(AgentState.Thinking);
                    }
                    break;
                case "speaking":
                    SetState(AgentState.Speaking);
                    break;
            }
        }

        void OnAgentAudio(float[] samples, int generation)
        {
            if (generation < currentGeneration) return; // a reply that has been superseded
            if (generation > currentGeneration)
            {
                currentGeneration = generation;
                Playback.Clear();
            }
            Playback.Write(samples);
            lastAgentAudioUtc = DateTime.UtcNow;
            if (state != AgentState.Speaking) SetState(AgentState.Speaking);
            replyAudioArrived = true;
        }

        void OnToolCall(string name, string arguments, string callId)
        {
            System.Threading.Interlocked.Exchange(ref replyOwedUntilTicks, DateTime.UtcNow.AddSeconds(8).Ticks);
            if (state != AgentState.Speaking) SetState(AgentState.Thinking);
            AgentAction action;
            lock (actions) actions.TryGetValue(name ?? "", out action);
            var args = new ActionArgs(arguments);
            Post(() =>
            {
                string result;
                if (action == null)
                {
                    result = "{\"error\":\"This game has no action called " + (name ?? "") + ".\"}";
                }
                else
                {
                    try { result = action.Handler(args) ?? "done"; }
                    catch (Exception e) { result = "{\"error\":" + MiniJson.Serialize(e.Message) + "}"; }
                }
                if (!string.IsNullOrEmpty(callId))
                    SendJson(Msg("tool_result", new Dictionary<string, object> { ["call_id"] = callId, ["result"] = result }));
                ActionCalled?.Invoke(name, args, result);
            });
        }

        void OnVisemes(List<object> data)
        {
            if (data == null) return;
            var marks = new List<VisemeMark>(data.Count);
            foreach (var o in data)
            {
                if (!(o is Dictionary<string, object> d)) continue;
                string c = Str(d, "c");
                char ch = string.IsNullOrEmpty(c) ? ' ' : c[0];
                marks.Add(new VisemeMark { Time = (float)Num(d, "t"), Char = ch, Shape = VisemeMark.ShapeFor(ch) });
            }
            Post(() => VisemesReceived?.Invoke(marks));
        }

        static string Str(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null ? v as string ?? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : "";

        static double Num(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v is double x ? x : 0;

        static int Int(Dictionary<string, object> d, string k) => (int)Num(d, k);

        // ---- Teardown ----------------------------------------------------------------------------

        public async Task DisconnectAsync()
        {
            var socket = ws;
            if (socket == null) return;
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using (var t = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", t.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) { }
            cts?.Cancel();
            try { if (receiveTask != null) await Task.WhenAny(receiveTask, Task.Delay(2000)).ConfigureAwait(false); } catch (Exception) { }
            socket.Dispose();
            ws = null;
            cts?.Dispose();
            cts = null;
            while (binaryOut.TryDequeue(out _)) { }
            while (textOut.TryDequeue(out _)) { }
            Playback.Clear();
            SetState(AgentState.Disconnected);
        }

        public void Dispose()
        {
            try { DisconnectAsync().Wait(3000); } catch (Exception) { }
        }
    }
}
