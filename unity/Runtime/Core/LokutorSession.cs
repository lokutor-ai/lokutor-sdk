// The engine-independent part of the Lokutor game SDK: one voice conversation with one character,
// on top of VoiceAgentClient. It knows what the character is doing, keeps its voice until the engine
// plays it, runs the game's actions, and decides when the microphone is open. No UnityEngine
// references, so it builds and is tested with plain .NET; LokutorAgent (Unity) is a thin layer on top.
using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>A mouth shape, coarse enough to map onto any sprite set. LipSync uses the 15 visemes themselves.</summary>
    public enum MouthShape { Rest, A, E, I, O, U, MBP, FV, L, S, Other }

    /// <summary>A viseme with the names it had before 0.2. <see cref="Viseme"/> is what LipSync takes.</summary>
    public struct VisemeMark
    {
        public float Time;
        public float Duration;
        /// <summary>Its id, 0-14.</summary>
        public int Id;
        /// <summary>Its name in LokutorApi.Visemes.</summary>
        public string Name;
        /// <summary>The first letter of its name.</summary>
        public char Char;
        public MouthShape Shape;

        static readonly MouthShape[] Shapes =
        {
            MouthShape.Rest, MouthShape.MBP, MouthShape.FV, MouthShape.L, MouthShape.L, MouthShape.Other, MouthShape.S, MouthShape.S,
            MouthShape.L, MouthShape.Other, MouthShape.A, MouthShape.E, MouthShape.I, MouthShape.O, MouthShape.U,
        };

        internal VisemeMark(Viseme v)
        {
            Time = v.T;
            Duration = v.D;
            Id = v.V;
            Name = v.C;
            Char = string.IsNullOrEmpty(v.C) ? ' ' : v.C[0];
            Shape = Shapes[v.V];
        }

        /// <summary>The mouth of a letter.</summary>
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
        /// the API key), so the key never ships inside the game. Called before every connect, on the thread
        /// that calls ConnectAsync, or Update when a dropped connection is opened again.
        /// </summary>
        public Func<Task<string>> TicketProvider;

        /// <summary>An agent built in the Lokutor dashboard. Persona, voice and language then default to it.</summary>
        public string AgentId;

        /// <summary>Who the character is and how it talks (the system prompt).</summary>
        public string Persona;

        /// <summary>The line above what SetContext tells the character. Write it in the language of the persona.</summary>
        public string ContextHeading = "Right now in the game:";

        /// <summary>"F1".."F5", "M1".."M5" (a vetted speaker in the call's language) or a voice id from GET /voices.</summary>
        public string Voice;

        /// <summary>
        /// "en", "es", "fr", "pt", "ca", "gl", "eu", ... or "auto" (or ""): the character follows the player's
        /// language, answering in whichever one the player speaks. null = the default ("en"; a dashboard
        /// agent keeps its own).
        /// </summary>
        public string Language;

        /// <summary>Shifts the voice up or down: small creatures, giants.</summary>
        public float Pitch;

        /// <summary>Receive the mouth shapes of the voice, for lip sync.</summary>
        public bool Visemes = true;

        /// <summary>Your own OpenAI-compatible language model.</summary>
        public CustomLLMConfig Llm;

        /// <summary>Your own language model, running in the game (see VoiceAgentClient.OnLlmRequest).</summary>
        public Func<LlmRequest, LlmReply, Task<string>> OnLlmRequest;

        /// <summary>Open the connection again when it drops. The character does not remember what came before.</summary>
        public bool AutoReconnect = true;

        /// <summary>
        /// Send silence instead of the microphone while the character's voice is playing, so laptop
        /// speakers do not make it interrupt itself. Turn off with headphones to let players interrupt.
        /// </summary>
        public bool MuteMicWhileSpeaking = true;

        /// <summary>
        /// The server has the character say "One moment" or the like while an action runs. False: it says nothing until its
        /// answer.
        /// </summary>
        public bool ActionFiller;

        /// <summary>
        /// What it begins to say before it calls an action is heard. False is for an action that it must call before it
        /// answers ("how funny that was"): the model can write its plan there.
        /// </summary>
        public bool SpeakBeforeActions = true;

        /// <summary>
        /// How long the microphone stays muted after the voice has finished playing: the room's echo
        /// still reaches it for a moment.
        /// </summary>
        public int EchoTailMs = 300;

        public string Url = LokutorApi.VoiceAgentUrl;
        public string UserAgent = "lokutor-game-sdk/" + LokutorApi.Version;
        public TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How events reach your code. Null (the default): they are queued and delivered when you call
        /// <see cref="LokutorSession.Update"/>, i.e. on your game's main thread.
        /// </summary>
        public Action<Action> Dispatcher;
    }

    public sealed class LokutorSession : IDisposable
    {
        public const int DefaultPlaybackRate = LokutorApi.OutputRate;
        public const int DefaultInputRate = LokutorApi.InputRate;

        const string DefaultPersona = "You are a friendly character in a video game. Keep answers short.";

        sealed class Pump : EventPump
        {
            public Action<Exception> Failed;
            public void Add(Action a) => Post(a);
            protected override void OnCallbackFailed(Exception e) => Failed(e);
        }

        readonly SessionOptions options;
        readonly Pump pump = new Pump();
        readonly Dictionary<string, AgentAction> actions = new Dictionary<string, AgentAction>();
        readonly List<KeyValuePair<string, string>> context = new List<KeyValuePair<string, string>>();
        long contextDueTicks;
        StreamResampler micResampler;

        // Written on the network thread, read on the game's.
        volatile AgentState state = AgentState.Disconnected;
        volatile bool replyAudioArrived;
        volatile bool replyOpen;        // a reply is being made or spoken; audio outside one is the agent's background sound
        volatile bool newReply;
        volatile float voiceEnd;        // when the reply's last mouth shape ends, in seconds of voice
        long lastAgentAudioTicks, speakingSinceTicks, lastVisemeTicks, playerTalkingSinceTicks;
        long audibleUntilTicks, echoUntilTicks, lastMicVoiceTicks;
        // The generation being held back (see ForgetFiller), and what came of it. Network thread and Update().
        readonly object fillerGate = new object();
        readonly List<Action> held = new List<Action>();
        bool turnHasVoice;              // something has been said since the player's last line
        int filler;
        int speaking, dropped;          // the generation being heard, and the one that will not be: see SpeakBeforeActions
        int turnActions;                // since the player's last line
        double heldSeconds;
        long actionTicks, heldSinceTicks, heldTextTicks;
        // After an action, the character still owes the reply that uses its result: until that reply's
        // text arrives (or 8 s), it is thinking, not done.
        long replyOwedUntilTicks;
        AgentState reportedState = AgentState.Disconnected;
        string endReason;
        volatile bool firstConnect;

        public LokutorSession(SessionOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            Playback = new PlaybackBuffer(DefaultPlaybackRate);
            pump.Failed = e => Error?.Invoke(new LokutorError("callback", e.Message, e.GetType().Name, false));
            // The client hands everything over as it arrives; the session decides what waits for Update().
            Client = new VoiceAgentClient { Dispatcher = a => a() };
            Client.Opened = () => firstConnect = false;
            Client.Status += OnStatus;
            Client.GenerationStarted += OnGeneration;
            Client.Audio += (pcm, gen) =>
            {
                var samples = Pcm16.ToFloat(pcm);
                if (!Hold(gen, () => OnAgentAudio(samples), samples.Length / (double)Client.PlaybackRate)) OnAgentAudio(samples);
            };
            Client.Visemes += (list, gen) =>
            {
                if (!Hold(gen, () => OnVisemes(list, gen), 0)) OnVisemes(list, gen);
            };
            Client.Transcription += t =>
            {
                lock (fillerGate) turnActions = 0;     // a line that the server does not announce with "listening"
                Post(() => PlayerSaid?.Invoke(t));
            };
            Client.TranscriptionPartial += t => Post(() => PlayerSaidPartial?.Invoke(t));
            Client.Response += t =>
            {
                Interlocked.Exchange(ref replyOwedUntilTicks, 0); // the reply after an action has come
                lock (fillerGate)
                {
                    actionTicks = 0;
                    if (filler != 0 && heldTextTicks == 0) heldTextTicks = DateTime.UtcNow.Ticks;
                }
                Post(() => AgentSaid?.Invoke(t));
            };
            Client.TranscriptTruncated += (heard, full) => Post(() => AgentInterrupted?.Invoke(heard));
            Client.ToolCall += OnToolCall;
            Client.SessionLimit += (seconds, reason) => Post(() => SessionLimit?.Invoke((int)seconds));
            Client.Error += e =>
            {
                // Disconnected carries the first, ConnectAsync throws the second.
                if (e.Code != "session.ended" && !firstConnect) RaiseError(e);
            };
            Client.Reconnected += () => Post(() => Reconnected?.Invoke());
            Client.Ended += (code, reason) => endReason = string.IsNullOrEmpty(reason) ? "closed (" + code + ")" : reason;
        }

        // ---- What the game reads -----------------------------------------------------------------

        /// <summary>See SessionOptions.MuteMicWhileSpeaking; it can change at any time.</summary>
        public bool MuteMicWhileSpeaking { get => options.MuteMicWhileSpeaking; set => options.MuteMicWhileSpeaking = value; }
        /// <summary>See SessionOptions.ActionFiller; it can change at any time.</summary>
        public bool ActionFiller { get => options.ActionFiller; set => options.ActionFiller = value; }
        /// <summary>See SessionOptions.SpeakBeforeActions; it can change at any time.</summary>
        public bool SpeakBeforeActions { get => options.SpeakBeforeActions; set => options.SpeakBeforeActions = value; }
        /// <summary>Other voices of the game that would reach the microphone: with MuteMicWhileSpeaking, it is muted while this is true.</summary>
        public Func<bool> OthersAudible;
        /// <summary>The layer underneath, for what the session does not cover.</summary>
        public VoiceAgentClient Client { get; }
        /// <summary>The character's voice. Pull from it in your audio callback at <see cref="PlaybackRate"/>.</summary>
        public PlaybackBuffer Playback { get; }
        public int PlaybackRate => Playback.SampleRate;
        public int InputRate => Client.InputRate;
        public AgentState State => state;
        public bool IsConnected => Client.Connected;
        public string SessionId => string.IsNullOrEmpty(Client.SessionId) ? null : Client.SessionId;
        /// <summary>Loudness of the character's voice right now, 0..1: drive a mouth or jaw with it.</summary>
        public float MouthOpen => Playback.Level;
        /// <summary>How much of the current reply has been heard, in seconds: the clock for visemes.</summary>
        public float HeardSeconds => Playback.HeardSeconds;
        /// <summary>The character's voice is being heard (not its background sound).</summary>
        public bool IsSpeaking => state == AgentState.Speaking || (replyOpen && Playback.IsAudible);

        // ---- Events (delivered on Update() unless a Dispatcher is set) -----------------------------

        public event Action<AgentState> StateChanged;
        /// <summary>What the player said, once the turn is over.</summary>
        public event Action<string> PlayerSaid;
        /// <summary>The player's words while they are still talking.</summary>
        public event Action<string> PlayerSaidPartial;
        /// <summary>What the character is about to say. LokutorApi.StripTags takes out [laugh] and the like for subtitles.</summary>
        public event Action<string> AgentSaid;
        /// <summary>The player cut the character off; this is what they actually heard.</summary>
        public event Action<string> AgentInterrupted;
        /// <summary>An action ran (after its handler returned): name, arguments, result.</summary>
        public event Action<string, ActionArgs, string> ActionCalled;
        /// <summary>A new reply begins, or the one being spoken was cut: a mouth should close.</summary>
        public event Action VoiceCleared;
        /// <summary>Mouth shapes of the reply, as they arrive.</summary>
        public event Action<IReadOnlyList<Viseme>> Visemes;
        /// <summary>The same with the names of version 0.1.</summary>
        public event Action<IReadOnlyList<VisemeMark>> VisemesReceived;
        public event Action<LokutorError> Error;
        /// <summary>The server announced how many seconds this session may still run.</summary>
        public event Action<int> SessionLimit;
        /// <summary>The connection dropped and is being opened again; then Reconnected, in a fresh conversation.</summary>
        public event Action Reconnecting;
        public event Action Reconnected;
        public event Action<string> Disconnected;

        // ---- Setup -----------------------------------------------------------------------------------

        /// <summary>Lets the character call a game function. Works before or during a conversation.</summary>
        public LokutorSession AddAction(AgentAction action)
        {
            lock (actions) actions[action.Name] = action;
            RefreshActions();
            return this;
        }

        public LokutorSession AddAction(string name, string description, Func<ActionArgs, string> handler, params ActionParam[] parameters)
            => AddAction(new AgentAction(name, description, handler, parameters));

        /// <summary>An action that takes a while: the character waits for the result.</summary>
        public LokutorSession AddActionAsync(string name, string description, Func<ActionArgs, Task<string>> handler, params ActionParam[] parameters)
            => AddAction(AgentAction.Async(name, description, handler, parameters));

        public void RemoveAction(string name)
        {
            lock (actions) actions.Remove(name);
            RefreshActions();
        }

        /// <summary>Sends the list of actions again. AddAction and RemoveAction do it for you.</summary>
        public void RefreshActions()
        {
            if (IsConnected) Client.UpdateTools(ToolList());
        }

        List<ToolDefinition> ToolList()
        {
            var list = new List<ToolDefinition>();
            lock (actions)
                foreach (var a in actions.Values) list.Add(a.ToTool());
            return list;
        }

        /// <summary>Opens the conversation. Throws IOException when it cannot.</summary>
        public async Task ConnectAsync(CancellationToken cancel = default)
        {
            if (state != AgentState.Disconnected) throw new InvalidOperationException("Already connected; call DisconnectAsync first.");
            if (string.IsNullOrEmpty(options.ApiKey) && options.TicketProvider == null)
                throw new InvalidOperationException("Set SessionOptions.ApiKey (or TicketProvider).");

            bool dashboard = !string.IsNullOrEmpty(options.AgentId);
            Client.ApiKey = options.ApiKey;
            Client.Auth = options.TicketProvider == null ? null : (Func<Task<string>>)GetTicket;
            Client.AgentId = options.AgentId;
            Client.ServerUrl = options.Url;
            Client.UserAgent = options.UserAgent;
            Client.ConnectTimeout = options.ConnectTimeout;
            Client.AutoReconnect = options.AutoReconnect;
            Client.Voice = options.Voice;
            Client.Language = ConfigLanguage(options.Language, dashboard);
            Client.Pitch = options.Pitch;
            Client.WantVisemes = options.Visemes;
            Client.Llm = options.Llm;
            Client.OnLlmRequest = options.OnLlmRequest == null ? null : (Func<LlmRequest, LlmReply, Task<string>>)((request, reply) => OnUpdate(() => options.OnLlmRequest(request, reply)));
            Client.Tools = ToolList();
            Client.Prompt = FullPrompt();

            SetState(AgentState.Connecting);
            Playback.Clear();
            micResampler = null;
            newReply = replyOpen = replyAudioArrived = false;
            voiceEnd = 0f;
            Interlocked.Exchange(ref replyOwedUntilTicks, 0);
            endReason = null;
            firstConnect = true;
            bool ok;
            using (cancel.Register(Client.Disconnect)) ok = await Client.ConnectAsync().ConfigureAwait(false);
            firstConnect = false;
            if (ok) return;
            SetState(AgentState.Disconnected);
            cancel.ThrowIfCancellationRequested();
            var e = Client.LastError;
            string why = e == null ? "" : e.Detail.Length > 0 ? e.Detail : e.Message;
            throw new IOException("Could not reach Lokutor (" + why + "). Check the API key and network.", e == null ? null : new LokutorException(e));
        }

        // The first ticket is asked for on the caller's thread; the ones for reconnecting, on Update().
        Task<string> GetTicket() => firstConnect ? options.TicketProvider() : OnUpdate(options.TicketProvider);

        Task<string> OnUpdate(Func<Task<string>> f)
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async () =>
            {
                try { done.TrySetResult(await f()); }
                catch (Exception e) { done.TrySetException(e); }
            });
            return done.Task;
        }

        /// <summary>
        /// The language to send at connect, or null to send none. null (not set) is "en", or the dashboard
        /// agent's own; "auto" and "" are sent as "", which makes the character follow the player's language.
        /// </summary>
        public static string ConfigLanguage(string language, bool dashboardAgent)
        {
            if (language == null) return dashboardAgent ? null : "en";
            return language == "auto" ? "" : language;
        }

        // ---- The character -------------------------------------------------------------------------

        string FullPrompt()
        {
            string p = options.Persona;
            if (string.IsNullOrEmpty(p))
            {
                // A dashboard agent keeps its own prompt, which the game cannot see or extend.
                if (!string.IsNullOrEmpty(options.AgentId)) return null;
                p = DefaultPersona;
            }
            lock (context)
            {
                if (context.Count == 0) return p;
                var sb = new StringBuilder(p).Append("\n\n").Append(options.ContextHeading);
                foreach (var c in context) sb.Append("\n- ").Append(c.Key).Append(": ").Append(c.Value);
                return sb.ToString();
            }
        }

        void PushPrompt()
        {
            string p = FullPrompt();
            if (p == null) return;
            if (IsConnected) Client.UpdatePrompt(p);
            else Client.Prompt = p;
        }

        /// <summary>Changes who the character is, mid-conversation.</summary>
        public void SetPersona(string persona)
        {
            options.Persona = persona;
            PushPrompt();
        }

        /// <summary>
        /// Tells the character something about the game right now ("Time left", "20 seconds"), without rewriting
        /// its persona. Null takes it out. Changes in the same quarter second are sent together, by Update(). A
        /// dashboard agent needs a Persona for this: its own prompt cannot be extended from the game.
        /// </summary>
        public void SetContext(string key, string value)
        {
            lock (context)
            {
                int i = context.FindIndex(c => c.Key == key);
                if (value == null)
                {
                    if (i < 0) return;
                    context.RemoveAt(i);
                }
                else if (i < 0) context.Add(new KeyValuePair<string, string>(key, value));
                else if (context[i].Value == value) return;
                else context[i] = new KeyValuePair<string, string>(key, value);
            }
            if (state != AgentState.Disconnected && Interlocked.Read(ref contextDueTicks) == 0)
                Interlocked.Exchange(ref contextDueTicks, DateTime.UtcNow.AddMilliseconds(250).Ticks);
        }

        public void SetVoice(string voice)
        {
            options.Voice = voice;
            if (state != AgentState.Disconnected) Client.UpdateVoice(voice);
        }

        /// <summary>"auto": the character follows the language the player speaks.</summary>
        public void SetLanguage(string language)
        {
            options.Language = language;
            if (state != AgentState.Disconnected) Client.UpdateLanguage(language);
        }

        public void SetPitch(float pitch)
        {
            options.Pitch = pitch;
            if (state != AgentState.Disconnected) Client.UpdatePitch(pitch);
        }

        /// <summary>Forgets the conversation so far (the character starts fresh).</summary>
        public void ResetConversation()
        {
            if (IsConnected) Client.Reset();
        }

        /// <summary>The conversation so far.</summary>
        public List<TranscriptLine> GetTranscript() => Client.GetTranscript();

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
                micResampler = new StreamResampler(sampleRate, InputRate);
            var resampled = micResampler.Process(samples, offset, count);
            if (resampled.Length == 0) return;
            if (options.MuteMicWhileSpeaking && (AgentAudible || (OthersAudible != null && OthersAudible()))) Array.Clear(resampled, 0, resampled.Length);
            NoteMicLevel(resampled);
            Client.SendAudio(Pcm16.FromFloat(resampled, 0, resampled.Length));
        }

        /// <summary>Sends 16 kHz mono PCM16 as is (e.g. a recorded line).</summary>
        public void SendPcm16(byte[] pcm16At16k)
        {
            if (!IsConnected || pcm16At16k == null || pcm16At16k.Length == 0) return;
            NoteMicLevel(Pcm16.ToFloat(pcm16At16k));
            Client.SendAudio(pcm16At16k);
        }

        void NoteMicLevel(float[] samples)
        {
            if (LokutorAudio.CalculateRMS(samples) > 0.02f) Interlocked.Exchange(ref lastMicVoiceTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Tells the session when the voice the engine has already taken from <see cref="Playback"/> will
        /// have finished coming out of the speakers (its own buffers and output latency included), so
        /// the microphone stays muted until then. Engines that do not call it get EchoTailMs after their
        /// last read, which is enough when they read close to real time.
        /// </summary>
        public void SetVoiceAudibleUntil(DateTime utc) => Interlocked.Exchange(ref audibleUntilTicks, utc.Ticks);

        // Voice waiting, voice in the engine's buffers, or the room's echo just after.
        bool SoundAudible
        {
            get
            {
                if (Playback.IsPlaying) return true;
                long until = Math.Max(Interlocked.Read(ref audibleUntilTicks), Playback.LastVoiceReadUtc.Ticks);
                return DateTime.UtcNow.Ticks < until + TimeSpan.FromMilliseconds(options.EchoTailMs).Ticks;
            }
        }

        /// <summary>The character can still be heard, so it could reach the microphone. Its background sound does not count.</summary>
        internal bool AgentAudible => (replyOpen && SoundAudible) || DateTime.UtcNow.Ticks < Interlocked.Read(ref echoUntilTicks);

        // ---- Main-thread pump --------------------------------------------------------------------

        /// <summary>
        /// Call once per frame from the game's main thread. Delivers queued events, runs action
        /// handlers, and moves the state from Speaking back to Ready once the voice has played out.
        /// </summary>
        public void Update()
        {
            var now = DateTime.UtcNow.Ticks;
            lock (fillerGate)
                // The reply to the action comes when what was said meanwhile has had time to be heard.
                if (filler != 0 && heldTextTicks != 0 && now > Math.Max(heldTextTicks + TimeSpan.TicksPerSecond * 3, heldSinceTicks + (long)(TimeSpan.TicksPerSecond * (heldSeconds + 2))))
                    PlayFiller();
            if (state == AgentState.Speaking && ReplyFinished(now))
            {
                // Still audible when an agent's background sound goes on: only the echo of the voice is left to wait for.
                Interlocked.Exchange(ref echoUntilTicks, SoundAudible ? DateTime.UtcNow.AddMilliseconds(options.EchoTailMs).Ticks : 0);
                replyOpen = false;
                SetState(ReplyOwed ? AgentState.Thinking : AgentState.Ready);
            }
            // The server says when the player starts talking, but not when a start led nowhere (a noise,
            // or speech it set aside). Without a reply under way, the player is done once the microphone
            // has been quiet for 4 s: the answer to a real line can take more than two to start.
            if (state == AgentState.PlayerTalking && Seconds(now, ref playerTalkingSinceTicks) > 4.5 && Seconds(now, ref lastMicVoiceTicks) > 4.0 && !ReplyOwed)
                SetState(AgentState.Ready);
            if (state == AgentState.Thinking && !ReplyOwed && Interlocked.Read(ref replyOwedUntilTicks) != 0)
            {
                // The reply owed after an action never came (8 s): stop waiting for it.
                Interlocked.Exchange(ref replyOwedUntilTicks, 0);
                SetState(AgentState.Ready);
            }
            long due = Interlocked.Read(ref contextDueTicks);
            if (due != 0 && now >= due)
            {
                Interlocked.Exchange(ref contextDueTicks, 0);
                PushPrompt();
            }
            pump.Update();
        }

        static double Seconds(long now, ref long since) => TimeSpan.FromTicks(now - Interlocked.Read(ref since)).TotalSeconds;

        bool ReplyOwed => DateTime.UtcNow.Ticks < Interlocked.Read(ref replyOwedUntilTicks);

        // A reply is over once its audio has arrived, nothing new has come for a moment, and it can no longer be
        // heard (engine buffers and the room's echo included): Ready then means the microphone is really open
        // again. An agent with a background sound keeps sending audio: there, its voice is over when its last
        // mouth shape has played.
        bool ReplyFinished(long now)
        {
            // The server says "speaking" when synthesis starts, which can be half a second before the first
            // audio; if no audio ever comes, give up after 5 s.
            if (!replyAudioArrived) return Seconds(now, ref speakingSinceTicks) > 5;
            double quiet = Math.Min(Seconds(now, ref lastAgentAudioTicks), Seconds(now, ref speakingSinceTicks));
            if (quiet > 0.4 && !SoundAudible) return true;
            return voiceEnd > 0f && Playback.HeardSeconds > voiceEnd + 0.6f && Seconds(now, ref lastVisemeTicks) > 1.5;
        }

        void Post(Action a)
        {
            pump.Dispatcher = options.Dispatcher;
            pump.Add(a);
        }

        void SetState(AgentState next)
        {
            if (next == AgentState.PlayerTalking && state != AgentState.PlayerTalking) Interlocked.Exchange(ref playerTalkingSinceTicks, DateTime.UtcNow.Ticks);
            if (next == AgentState.Speaking && state != AgentState.Speaking) Interlocked.Exchange(ref speakingSinceTicks, DateTime.UtcNow.Ticks);
            state = next;
            Post(() =>
            {
                if (reportedState == next) return;
                reportedState = next;
                StateChanged?.Invoke(next);
            });
        }

        void RaiseError(LokutorError e) => Post(() => Error?.Invoke(e));

        // ---- What the network brings -------------------------------------------------------------

        /// <summary>Takes the text frames sent without a socket. For tests.</summary>
        internal List<string> DrainOutgoingTextForTests()
        {
            lock (Client.UnsentForTests)
            {
                var list = new List<string>(Client.UnsentForTests);
                Client.UnsentForTests.Clear();
                return list;
            }
        }

        /// <summary>Handles one JSON frame from the server. Internal so tests can feed it frames.</summary>
        internal void HandleServerMessage(string json)
        {
            Client.HandleMessage(json);
            MatchRate();
        }

        void MatchRate()
        {
            if (Client.PlaybackRate == Playback.SampleRate) return;
            Playback.Clear();
            Playback.SampleRate = Client.PlaybackRate;
        }

        // What the server has it say while an action runs comes right after the action, before any text, and the reply to
        // the action is another generation. It is held until that one comes. Should it turn out to be the reply (it is too
        // long, or its text comes and nothing follows), it is played.
        void ForgetFiller()
        {
            filler = 0;
            held.Clear();
            heldSeconds = 0;
            heldTextTicks = 0;
        }

        bool Hold(int generation, Action later, double seconds)
        {
            lock (fillerGate)
            {
                if (dropped != 0 && generation == dropped) return true;
                if (filler == 0 || generation != filler) return false;
                held.Add(later);
                heldSeconds += seconds;
                if (heldSeconds > 15) PlayFiller();
                return true;
            }
        }

        void PlayFiller()
        {
            var later = held.ToArray();
            int generation = filler;
            ForgetFiller();
            actionTicks = 0;
            OnGeneration(generation);
            foreach (var a in later) a();
        }

        void OnGeneration(int generation)
        {
            lock (fillerGate)
            {
                ForgetFiller();
                dropped = 0;
                if (DateTime.UtcNow.Ticks - actionTicks < TimeSpan.TicksPerMillisecond * 400)
                {
                    filler = generation;
                    heldSinceTicks = DateTime.UtcNow.Ticks;
                    return;
                }
                turnHasVoice = true;
                speaking = generation;
            }
            // A new reply: whatever was still queued belongs to the old one.
            MatchRate();
            Playback.Clear();
            voiceEnd = 0f;
            newReply = true;
            replyOpen = true;
            replyAudioArrived = false;
            Post(() => VoiceCleared?.Invoke());
        }

        void OnStatus(string status)
        {
            lock (fillerGate)
            {
                if (status == "listening" || status == "interrupted")
                {
                    turnHasVoice = false;
                    turnActions = 0;
                    actionTicks = 0;
                    if (filler != 0)           // the player goes on talking: the reply may never come, and this is not one
                    {
                        dropped = filler;
                        ForgetFiller();
                    }
                }
                if (status == "interrupted")
                {
                    ForgetFiller();
                    dropped = 0;
                }
                if ((filler != 0 || dropped != 0) && (status == "thinking" || status == "speaking"))
                {
                    SetState(AgentState.Thinking);
                    return;
                }
            }
            switch (status)
            {
                case "connected":
                    MatchRate();
                    SetState(AgentState.Ready);
                    break;
                case "listening":
                    // The player started talking. Not an interruption yet: it may be a cough.
                    if (state != AgentState.Speaking) SetState(AgentState.PlayerTalking);
                    break;
                case "interrupted":
                    CutVoice();
                    SetState(AgentState.PlayerTalking);
                    break;
                case "thinking":
                    // The server says "thinking" again before each sentence of the same reply; only a
                    // character that is not already talking is thinking.
                    if (newReply || (state != AgentState.Speaking && !Playback.IsPlaying)) SetState(AgentState.Thinking);
                    newReply = false;
                    break;
                case "speaking":
                    replyOpen = true;
                    SetState(AgentState.Speaking);
                    break;
                case "reconnecting":
                    CutVoice();
                    Interlocked.Exchange(ref replyOwedUntilTicks, 0);
                    SetState(AgentState.Connecting);
                    Post(() => Reconnecting?.Invoke());
                    break;
                case "disconnected":
                    if (firstConnect) break;          // ConnectAsync reports it
                    CutVoice();
                    SetState(AgentState.Disconnected);
                    string reason = endReason ?? "connection lost";
                    Post(() => Disconnected?.Invoke(reason));
                    break;
            }
        }

        void CutVoice()
        {
            Playback.Clear();
            replyOpen = false;
            Post(() => VoiceCleared?.Invoke());
        }

        void OnAgentAudio(float[] samples)
        {
            MatchRate();
            Playback.Write(samples);
            if (!replyOpen) return;
            Interlocked.Exchange(ref lastAgentAudioTicks, DateTime.UtcNow.Ticks);
            newReply = false;
            if (state != AgentState.Speaking) SetState(AgentState.Speaking);
            replyAudioArrived = true;
        }

        void OnToolCall(ToolCall call)
        {
            Interlocked.Exchange(ref replyOwedUntilTicks, DateTime.UtcNow.AddSeconds(8).Ticks);
            bool cut = false;
            lock (fillerGate)
            {
                turnActions++;
                if (!options.ActionFiller && filler == 0 && !turnHasVoice) actionTicks = DateTime.UtcNow.Ticks;
                else if (!options.SpeakBeforeActions && turnActions == 1 && filler == 0 && replyOpen && replyAudioArrived && Playback.HeardSeconds < 1.5f)
                {
                    dropped = speaking;
                    cut = true;
                }
            }
            if (cut)
            {
                CutVoice();
                SetState(AgentState.Thinking);
            }
            if (state != AgentState.Speaking) SetState(AgentState.Thinking);
            AgentAction action;
            lock (actions) actions.TryGetValue(call.Name, out action);
            var args = new ActionArgs(call.Arguments);
            Post(() => RunAction(action, call, args));
        }

        async void RunAction(AgentAction action, ToolCall call, ActionArgs args)
        {
            string result;
            if (action == null)
            {
                result = "{\"error\":\"This game has no action called " + call.Name + ".\"}";
            }
            else
            {
                try { result = await action.Handler(args) ?? "done"; }
                catch (Exception e) { result = "{\"error\":" + MiniJson.Serialize(e.Message) + "}"; }
            }
            if (call.CallId.Length > 0) Client.SubmitToolResult(call.CallId, result);
            try { ActionCalled?.Invoke(call.Name, args, result); }
            catch (Exception e) { pump.Failed(e); }
        }

        void OnVisemes(IReadOnlyList<Viseme> list, int generation)
        {
            if (list.Count > 0)
            {
                Interlocked.Exchange(ref lastVisemeTicks, DateTime.UtcNow.Ticks);
                var last = list[list.Count - 1];
                voiceEnd = Math.Max(voiceEnd, last.T + last.D);
            }
            Post(() =>
            {
                Visemes?.Invoke(list);
                if (VisemesReceived == null) return;
                var marks = new List<VisemeMark>(list.Count);
                foreach (var v in list) marks.Add(new VisemeMark(v));
                VisemesReceived(marks);
            });
        }

        // ---- Teardown ----------------------------------------------------------------------------

        public async Task DisconnectAsync()
        {
            if (endReason == null) endReason = "disconnected";
            await Client.DisconnectAsync().ConfigureAwait(false);
            Playback.Clear();
            replyOpen = false;
            if (state != AgentState.Disconnected) SetState(AgentState.Disconnected);
        }

        public void Dispose()
        {
            try { DisconnectAsync().Wait(3000); } catch (Exception) { }
        }
    }
}
