// A voice conversation over wss://api.lokutor.com/ws/agent, with the same options, methods and events as VoiceAgentClient
// in the JavaScript and Python SDKs. No UnityEngine references. For a character in a scene use LokutorAgent, which adds
// the microphone, the 3D voice, lip sync and actions on top of LokutorSession, which is on top of this.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    /// <summary>A function the agent may call (OpenAI format).</summary>
    public sealed class ToolDefinition
    {
        public string Name, Description;
        /// <summary>A JSON schema: {"type": "object", "properties": {...}, "required": [...]}.</summary>
        public Dictionary<string, object> Parameters;

        public ToolDefinition() { }

        public ToolDefinition(string name, string description, Dictionary<string, object> parameters = null)
        {
            Name = name;
            Description = description;
            Parameters = parameters;
        }

        internal Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object>
            {
                ["name"] = Name,
                ["description"] = Description ?? "",
                ["parameters"] = Parameters ?? new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>() },
            },
        };
    }

    /// <summary>The agent wants a function called. Answer with SubmitToolResult(CallId, result).</summary>
    public sealed class ToolCall
    {
        public string Name;
        /// <summary>A JSON object.</summary>
        public string Arguments;
        public string CallId;
    }

    /// <summary>A line of the conversation.</summary>
    public sealed class TranscriptLine
    {
        /// <summary>"user" or "agent".</summary>
        public string Role;
        public string Text;
        /// <summary>Unix time in milliseconds.</summary>
        public long Timestamp;
    }

    /// <summary>Your own OpenAI-compatible endpoint.</summary>
    public sealed class CustomLLMConfig
    {
        public string BaseUrl, ApiKey, Model;
        public Dictionary<string, string> Headers;
        /// <summary>"auto" or "none".</summary>
        public string Tools;
        public bool? SpeculativeLlm;

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { ["base_url"] = BaseUrl };
            if (!string.IsNullOrEmpty(ApiKey)) d["api_key"] = ApiKey;
            if (!string.IsNullOrEmpty(Model)) d["model"] = Model;
            if (Headers != null) d["headers"] = Headers;
            if (!string.IsNullOrEmpty(Tools)) d["tools"] = Tools;
            if (SpeculativeLlm.HasValue) d["speculative_llm"] = SpeculativeLlm.Value;
            return d;
        }
    }

    /// <summary>One turn for a model that runs in the game: the conversation so far (OpenAI chat messages) and the agent's tools.</summary>
    public sealed class LlmRequest
    {
        public string RequestId;
        public List<Dictionary<string, object>> Messages = new List<Dictionary<string, object>>();
        public List<Dictionary<string, object>> Tools = new List<Dictionary<string, object>>();
    }

    /// <summary>The reply of your own model to one LlmRequest. Stop when Cancelled turns true: the player spoke over it.</summary>
    public sealed class LlmReply
    {
        readonly VoiceAgentClient client;
        readonly string id;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        bool finished;

        internal LlmReply(VoiceAgentClient client, string id)
        {
            this.client = client;
            this.id = id;
        }

        public bool Cancelled => cancel.IsCancellationRequested;
        public CancellationToken Token => cancel.Token;

        internal void Cancel() => cancel.Cancel();

        /// <summary>More text. Lokutor starts speaking at the first sentence.</summary>
        public void Chunk(string text)
        {
            if (!string.IsNullOrEmpty(text)) Send(new Dictionary<string, object> { ["type"] = "llm_chunk", ["data"] = text });
        }

        /// <summary>One of the agent's tools to call. Arguments: a JSON string, or anything JSON can carry.</summary>
        public void ToolCall(string name, object arguments, string callId = "")
        {
            Send(new Dictionary<string, object>
            {
                ["type"] = "llm_tool_call",
                ["id"] = string.IsNullOrEmpty(callId) ? "call_" + id + "_" + name : callId,
                ["name"] = name,
                ["arguments"] = arguments as string ?? MiniJson.Serialize(arguments ?? new Dictionary<string, object>()),
            });
        }

        public void Done()
        {
            Send(new Dictionary<string, object> { ["type"] = "llm_done" });
            finished = true;
        }

        public void Fail(string message)
        {
            message = message ?? "";
            Send(new Dictionary<string, object> { ["type"] = "llm_error", ["data"] = message.Length > 300 ? message.Substring(0, 300) : message });
            finished = true;
        }

        void Send(Dictionary<string, object> msg)
        {
            if (Cancelled || finished) return;
            msg["request_id"] = id;
            client.SendIfOpen(msg);
        }
    }

    /// <summary>A microphone and a speaker for a managed conversation (AudioManager in the JavaScript SDK). LokutorAudioManager is Unity's.</summary>
    public interface IAudioManager
    {
        /// <summary>Captures: mono samples, how many, and their rate. False when there is no microphone.</summary>
        bool StartMicrophone(Action<float[], int, int> onAudio);
        void StopMicrophone();
        /// <summary>Queues voice (16-bit PCM, mono).</summary>
        void PlayAudio(byte[] pcm16, int sampleRate);
        /// <summary>Stops at once and forgets what was queued.</summary>
        void StopPlayback();
        bool Muted { get; set; }
        /// <summary>The microphone's loudness, 0..1.</summary>
        float GetAmplitude();
    }

    public sealed class VoiceAgentClient : EventPump, IDisposable
    {
        // ---- Options -------------------------------------------------------------------------------

        /// <summary>An API key, or a wst_ ticket. Fine while prototyping; a shipped game should set Auth.</summary>
        public string ApiKey;
        /// <summary>Called before every connect and reconnect for the credential to use: have it ask your server for a ticket (POST https://api.lokutor.com/ws-ticket).</summary>
        public Func<Task<string>> Auth;
        /// <summary>An agent from the dashboard. Then voice, language and prompt are sent only if you set them.</summary>
        public string AgentId;
        public string ServerUrl = LokutorApi.VoiceAgentUrl;
        public string Prompt;
        /// <summary>Null: F1, or the dashboard agent's.</summary>
        public string Voice;
        /// <summary>"auto" or "": the agent follows the player's language. Null: "en", or the dashboard agent's.</summary>
        public string Language;
        /// <summary>Shifts the voice up or down.</summary>
        public float Pitch;
        public List<ToolDefinition> Tools = new List<ToolDefinition>();
        /// <summary>Ask for mouth shapes.</summary>
        public bool WantVisemes;
        /// <summary>Your own OpenAI-compatible endpoint.</summary>
        public CustomLLMConfig Llm;
        /// <summary>Your model runs in the game: return the reply, or write it with reply.Chunk(text) as it is produced and return null.</summary>
        public Func<LlmRequest, LlmReply, Task<string>> OnLlmRequest;
        /// <summary>"none": the agent's tools are not sent to OnLlmRequest.</summary>
        public string LlmTools = "auto";
        /// <summary>Open the connection again when it drops (2 s, 4 s, 8 s, 10 s, 10 s).</summary>
        public bool AutoReconnect = true;
        public int MaxReconnectAttempts = 5;
        public TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        public string UserAgent = "lokutor-csharp/" + LokutorApi.Version;

        // ---- Events (raised by Update() unless a Dispatcher is set) ----------------------------------

        /// <summary>A final line of player speech.</summary>
        public event Action<string> Transcription;
        /// <summary>The player's words while they are still talking. Not kept in GetTranscript().</summary>
        public event Action<string> TranscriptionPartial;
        /// <summary>What the agent is about to say.</summary>
        public event Action<string> Response;
        /// <summary>The agent's voice (16-bit PCM, mono, at PlaybackRate) and its generation. Audio of superseded replies never gets here.</summary>
        public event Action<byte[], int> Audio;
        /// <summary>Mouth shapes of the reply, and its generation.</summary>
        public event Action<IReadOnlyList<Viseme>, int> Visemes;
        /// <summary>connected, listening, thinking, speaking, interrupted, acknowledgements, and the client's own reconnecting and disconnected.</summary>
        public event Action<string> Status;
        public event Action<LokutorError> Error;
        public event Action<ToolCall> ToolCall;
        /// <summary>A new reply has begun: whatever was queued of the previous one must not be played.</summary>
        public event Action<int> GenerationStarted;
        /// <summary>The player cut the agent off: what they heard, and the whole line.</summary>
        public event Action<string, string> TranscriptTruncated;
        /// <summary>The player's paused sentence and its continuation became one line: the new text, and the one it replaces.</summary>
        public event Action<string, string> TranscriptRevised;
        /// <summary>How many seconds this session may still run, when the server sets a limit, and why.</summary>
        public event Action<float, string> SessionLimit;
        /// <summary>A dropped connection was opened again. It is a new server session: the agent does not remember the old one.</summary>
        public event Action Reconnected;
        /// <summary>The server ended the session on purpose (close code, reason); no reconnection follows.</summary>
        public event Action<int, string> Ended;

        // ---- State -----------------------------------------------------------------------------------

        /// <summary>The reply being spoken. Grows by one with each reply of a session.</summary>
        public int Generation { get; private set; }
        public string SessionId { get; private set; } = "";
        /// <summary>Rate of the agent's audio, announced by the server.</summary>
        public int PlaybackRate { get; private set; } = LokutorApi.OutputRate;
        public int InputRate { get; private set; } = LokutorApi.InputRate;
        public bool Connected { get { var s = socket; return s != null && s.IsOpen; } }
        /// <summary>Why the last connection failed.</summary>
        public LokutorError LastError { get; private set; }

        readonly object gate = new object();
        readonly List<TranscriptLine> messages = new List<TranscriptLine>();
        readonly Dictionary<string, LlmReply> replies = new Dictionary<string, LlmReply>();
        volatile LokutorSocket socket;
        CancellationTokenSource attempt;          // connecting, or waiting to
        Task<bool> connecting;
        int epoch, attempts;
        StreamResampler resampler;
        IAudioManager managed;
        bool managedWired;

        /// <summary>A connection is open, and nothing has been sent on it yet.</summary>
        internal Action Opened;
        /// <summary>What was sent while there was no connection. For tests.</summary>
        internal readonly Queue<string> UnsentForTests = new Queue<string>();

        public VoiceAgentClient() { }

        public VoiceAgentClient(string apiKey, string prompt = null, string voice = null, string language = null)
        {
            ApiKey = apiKey;
            Prompt = prompt;
            Voice = voice;
            Language = language;
        }

        // ---- Connection ------------------------------------------------------------------------------

        /// <summary>
        /// Opens the session and sends the configuration. False if it could not: the reason is raised as Error and kept
        /// in LastError.
        /// </summary>
        public Task<bool> ConnectAsync()
        {
            TaskCompletionSource<bool> done;
            CancellationToken cancel;
            int mine;
            lock (gate)
            {
                if (Connected) return Task.FromResult(true);
                if (connecting != null && !connecting.IsCompleted) return connecting;
                mine = ++epoch;
                attempts = 0;
                attempt?.Cancel();
                attempt = new CancellationTokenSource();
                cancel = attempt.Token;
                done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                connecting = done.Task;
            }
            FirstConnectAsync(mine, cancel).ContinueWith(t => done.TrySetResult(t.Status == TaskStatus.RanToCompletion && t.Result), TaskScheduler.Default);
            return done.Task;
        }

        async Task<bool> FirstConnectAsync(int mine, CancellationToken cancel)
        {
            var err = await DialAsync(mine, Auth, cancel).ConfigureAwait(false);
            if (err == null) return true;
            LastError = err;
            if (err.Code != "internal.cancelled")
            {
                RaiseError(err);
                RaiseStatus("disconnected");
            }
            return false;
        }

        async Task<LokutorError> DialAsync(int mine, Func<Task<string>> auth, CancellationToken cancel)
        {
            var query = new List<string>();
            if (!string.IsNullOrEmpty(AgentId)) query.Add("agent_id=" + Uri.EscapeDataString(AgentId));
            var opened = await LokutorSocket.OpenAsync(ServerUrl, ApiKey, auth, query, UserAgent, ConnectTimeout, cancel).ConfigureAwait(false);
            if (opened.Error != null) return opened.Error;
            var s = opened.Socket;
            bool current;
            lock (gate)
            {
                current = mine == epoch;
                if (current)
                {
                    // Every socket is a new server session, and a new session numbers its replies from 1.
                    Generation = 0;
                    s.OnText = t => { if (socket == s) HandleMessage(t); };
                    s.OnBinary = b =>
                    {
                        int gen = Generation;
                        if (socket == s) Post(() => Audio?.Invoke(b, gen));
                    };
                    s.OnClosed = (code, reason) => OnClosed(s, code, reason);
                    socket = s;
                    Opened?.Invoke();
                    foreach (var m in ConfigMessages()) s.SendText(MiniJson.Serialize(m));
                    s.Start();
                }
            }
            if (current) return null;
            await s.CloseAsync().ConfigureAwait(false);
            return new LokutorError("internal.cancelled", "Closed while connecting.");
        }

        void OnClosed(LokutorSocket s, int code, string reason)
        {
            int mine;
            lock (gate)
            {
                if (socket != s) return;
                socket = null;
                mine = epoch;
            }
            CancelReplies();
            if (LokutorApi.Reconnectable(code))
            {
                Retry(mine, reason);
                return;
            }
            Post(() => Ended?.Invoke(code, reason));
            var err = new LokutorError("session.ended", string.IsNullOrEmpty(reason) ? "The session was ended by the server." : reason, "close code " + code);
            LastError = err;
            RaiseError(err);
            StopManaged();
            RaiseStatus("disconnected");
        }

        void Retry(int mine, string why)
        {
            CancellationToken cancel;
            int wait;
            lock (gate)
            {
                if (mine != epoch) return;
                if (!AutoReconnect || attempts >= MaxReconnectAttempts) wait = -1;
                else wait = (int)Math.Min(1000.0 * Math.Pow(2, ++attempts), 10000.0);
                attempt?.Cancel();
                attempt = new CancellationTokenSource();
                cancel = attempt.Token;
            }
            if (wait < 0)
            {
                LastError = new LokutorError("ws.close", "The connection was lost.", attempts + " reconnection attempts" + (string.IsNullOrEmpty(why) ? "" : "; " + why), true);
                RaiseError(LastError);
                StopManaged();
                RaiseStatus("disconnected");
                return;
            }
            RaiseStatus("reconnecting");
            Task.Run(async () =>
            {
                try { await Task.Delay(wait, cancel).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                // Your auth callback runs on the game's thread, as the first time.
                var err = await DialAsync(mine, Auth == null ? null : (Func<Task<string>>)(() => OnEventThread(Auth)), cancel).ConfigureAwait(false);
                if (err == null)
                {
                    lock (gate) attempts = 0;
                    Post(() => Reconnected?.Invoke());
                }
                else if (err.Code == "internal.cancelled") return;
                else if (err.Retryable) Retry(mine, err.Message);
                else
                {
                    LastError = err;
                    RaiseError(err);
                    StopManaged();
                    RaiseStatus("disconnected");
                }
            });
        }

        Task<string> OnEventThread(Func<Task<string>> f)
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async () =>
            {
                try { done.TrySetResult(await f()); }
                catch (Exception e) { done.TrySetException(e); }
            });
            return done.Task;
        }

        /// <summary>Ends the session and releases the microphone.</summary>
        public void Disconnect() => _ = DisconnectAsync();

        public async Task DisconnectAsync()
        {
            LokutorSocket s;
            bool was;
            lock (gate)
            {
                epoch++;
                s = socket;
                socket = null;
                was = s != null || (attempt != null && !attempt.IsCancellationRequested && attempts > 0);
                attempt?.Cancel();
                connecting = null;
                attempts = 0;
            }
            StopManaged();
            CancelReplies();
            if (was) RaiseStatus("disconnected");
            if (s != null) await s.CloseAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            try { DisconnectAsync().Wait(3000); } catch (Exception) { }
        }

        // ---- Managed audio ---------------------------------------------------------------------------

        /// <summary>Runs the microphone and the speaker too: a whole conversation in one call.</summary>
        public async Task<bool> StartManagedAsync(IAudioManager audio)
        {
            managed = audio ?? throw new ArgumentNullException(nameof(audio));
            if (!managedWired)
            {
                managedWired = true;
                Audio += (pcm, gen) => managed?.PlayAudio(pcm, PlaybackRate);
                GenerationStarted += gen => managed?.StopPlayback();
                Status += s => { if (s == "interrupted" || s == "reconnecting") managed?.StopPlayback(); };
            }
            if (!await ConnectAsync()) return false;
            if (audio.StartMicrophone((samples, count, rate) => SendAudioFloat(samples, 0, count, rate))) return true;
            RaiseError(new LokutorError("audio.microphone_unavailable", "The microphone could not be started."));
            Disconnect();
            return false;
        }

        void StopManaged()
        {
            var a = managed;
            if (a == null) return;
            managed = null;
            Post(() =>
            {
                a.StopMicrophone();
                a.StopPlayback();
            });
        }

        /// <summary>Mutes or unmutes the managed microphone; returns whether it is now muted.</summary>
        public bool ToggleMute()
        {
            if (managed == null) return false;
            return managed.Muted = !managed.Muted;
        }

        /// <summary>The managed microphone's loudness, 0..1.</summary>
        public float GetAmplitude() => managed != null ? managed.GetAmplitude() : 0f;

        // ---- What the game sends -----------------------------------------------------------------------

        /// <summary>Microphone audio: 16-bit PCM, 16 kHz, mono. Keep sending it (silence too): the server measures pauses in it.</summary>
        public void SendAudio(byte[] pcm16)
        {
            var s = socket;
            if (s != null && pcm16 != null && pcm16.Length > 0) s.SendBinary(pcm16);
        }

        /// <summary>The same from float samples at any rate.</summary>
        public void SendAudioFloat(float[] samples, int rate) => SendAudioFloat(samples, 0, samples.Length, rate);

        public void SendAudioFloat(float[] samples, int offset, int count, int rate)
        {
            if (socket == null || count <= 0) return;
            if (resampler == null || resampler.InputRate != rate || resampler.OutputRate != InputRate) resampler = new StreamResampler(rate, InputRate);
            var r = resampler.Process(samples, offset, count);
            if (r.Length > 0) SendAudio(Pcm16.FromFloat(r, 0, r.Length));
        }

        public void UpdatePrompt(string prompt) { lock (gate) { Prompt = prompt; SendIfOpen(Json.Msg("prompt", prompt ?? "")); } }

        public void UpdateVoice(string voice) { lock (gate) { Voice = voice; SendIfOpen(Json.Msg("voice", voice)); } }

        public void UpdateLanguage(string language) { lock (gate) { Language = language ?? ""; SendIfOpen(Json.Msg("language", LokutorApi.LanguageToWire(language))); } }

        public void UpdateTools(IEnumerable<ToolDefinition> tools)
        {
            lock (gate)
            {
                Tools = new List<ToolDefinition>(tools);
                SendIfOpen(Json.Msg("tools", ToolsJson()));
            }
        }

        /// <summary>Shifts the voice's pitch for the rest of the session.</summary>
        public void UpdatePitch(float pitch) { lock (gate) { Pitch = pitch; SendIfOpen(Json.Msg("pitch", pitch)); } }

        /// <summary>Asks for visemes, or stops asking, from the next reply on.</summary>
        public void UpdateVisemes(bool enabled) { lock (gate) { WantVisemes = enabled; SendIfOpen(Json.Msg("visemes", enabled)); } }

        /// <summary>Clears the conversation: the agent starts fresh.</summary>
        public void Reset()
        {
            lock (messages) messages.Clear();
            SendIfOpen(new Dictionary<string, object> { ["type"] = "reset" });
        }

        /// <summary>Answers a ToolCall with a string, or anything JSON can carry.</summary>
        public void SubmitToolResult(string callId, object result) =>
            SendIfOpen(Json.Msg("tool_result", new Dictionary<string, object> { ["call_id"] = callId, ["result"] = result as string ?? MiniJson.Serialize(result) }));

        internal void SendIfOpen(Dictionary<string, object> msg)
        {
            string text = MiniJson.Serialize(msg);
            var s = socket;
            if (s != null) { s.SendText(text); return; }
            lock (UnsentForTests)
            {
                UnsentForTests.Enqueue(text);
                if (UnsentForTests.Count > 32) UnsentForTests.Dequeue();
            }
        }

        /// <summary>The conversation so far.</summary>
        public List<TranscriptLine> GetTranscript()
        {
            var copy = new List<TranscriptLine>();
            lock (messages)
                foreach (var m in messages) copy.Add(new TranscriptLine { Role = m.Role, Text = m.Text, Timestamp = m.Timestamp });
            return copy;
        }

        public string GetTranscriptText()
        {
            var sb = new StringBuilder();
            foreach (var m in GetTranscript())
                sb.Append(sb.Length > 0 ? "\n" : "").Append(m.Role == "user" ? "You: " : "Agent: ").Append(m.Text);
            return sb.ToString();
        }

        // ---- Discovery, as in the other SDKs -----------------------------------------------------------

        public static Task<List<VoiceInfo>> GetVoicesAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetVoicesAsync(baseUrl);
        public static Task<List<LanguageInfo>> GetLanguagesAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetLanguagesAsync(baseUrl);
        public static Task<List<ModelInfo>> GetModelsAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetModelsAsync(baseUrl);
        public static Task<ServerConfig> GetConfigAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetConfigAsync(baseUrl);
        public static Task<Dictionary<string, object>> GetStatusAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetStatusAsync(baseUrl);
        public static Task<HealthStatus> GetHealthAsync(string baseUrl = LokutorApi.VoiceAgentUrl) => LokutorApi.GetHealthAsync(baseUrl);

        // ---- Protocol ------------------------------------------------------------------------------------

        List<object> ToolsJson()
        {
            var list = new List<object>();
            foreach (var t in Tools) list.Add(t.ToJson());
            return list;
        }

        /// <summary>The configuration sent on connect, in order. "rates" is last: it starts the conversation.</summary>
        internal List<Dictionary<string, object>> ConfigMessages()
        {
            var list = new List<Dictionary<string, object>>();
            bool dashboard = !string.IsNullOrEmpty(AgentId);
            if (!string.IsNullOrEmpty(Voice) || !dashboard) list.Add(Json.Msg("voice", string.IsNullOrEmpty(Voice) ? "F1" : Voice));
            if (Language != null || !dashboard) list.Add(Json.Msg("language", Language == null ? "en" : LokutorApi.LanguageToWire(Language)));
            if (Tools != null && Tools.Count > 0) list.Add(Json.Msg("tools", ToolsJson()));
            list.Add(Json.Msg("visemes", WantVisemes));
            if (Pitch != 0f) list.Add(Json.Msg("pitch", Pitch));
            if (Llm != null) list.Add(Json.Msg("llm", Llm.ToJson()));
            else if (OnLlmRequest != null) list.Add(Json.Msg("llm", new Dictionary<string, object> { ["mode"] = "client", ["tools"] = LlmTools }));
            if (!string.IsNullOrEmpty(Prompt) || !dashboard) list.Add(Json.Msg("prompt", Prompt ?? ""));
            list.Add(new Dictionary<string, object> { ["type"] = "rates", ["playback"] = LokutorApi.OutputRate, ["input"] = LokutorApi.InputRate });
            return list;
        }

        /// <summary>Handles one message from the server. Internal so tests can feed it.</summary>
        internal void HandleMessage(string json)
        {
            Dictionary<string, object> msg;
            try { msg = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (FormatException) { return; }
            if (msg == null) return;
            msg.TryGetValue("data", out var data);
            string text = data as string ?? "";
            switch (Json.Str(msg, "type"))
            {
                case "audio":
                {
                    int gen = Json.Int(msg, "generation", Generation);
                    if (text.Length == 0 || gen < Generation) break;
                    byte[] pcm;
                    try { pcm = Convert.FromBase64String(text); } catch (FormatException) { break; }
                    Advance(gen);
                    Post(() => Audio?.Invoke(pcm, gen));
                    break;
                }
                case "visemes":
                case "viseme":
                {
                    int gen = Json.Int(msg, "generation", Generation);
                    if (gen < Generation) break;
                    var list = Viseme.FromServer(data ?? msg);
                    if (list.Count == 0 && !(data is List<object>)) break;
                    Advance(gen);
                    Post(() => Visemes?.Invoke(list, gen));
                    break;
                }
                case "server_rates":
                    if (Json.Int(msg, "playback") > 0) PlaybackRate = Json.Int(msg, "playback");
                    if (Json.Int(msg, "input") > 0) InputRate = Json.Int(msg, "input");
                    break;
                case "status":
                    if (text == "connected" && Json.Str(msg, "session_id").Length > 0) SessionId = Json.Str(msg, "session_id");
                    if (text == "thinking") Advance(Json.Int(msg, "generation"));
                    RaiseStatus(text);
                    break;
                case "transcript":
                {
                    string line = data != null ? text : Json.Str(msg, "text");
                    if (Json.Bool(msg, "partial")) Post(() => TranscriptionPartial?.Invoke(line));
                    else if (Json.Str(msg, "role", "user") == "agent")
                    {
                        Remember("agent", line);
                        Post(() => Response?.Invoke(line));
                    }
                    else
                    {
                        Remember("user", line);
                        Post(() => Transcription?.Invoke(line));
                    }
                    break;
                }
                case "transcript_truncated":
                {
                    string full = Json.Str(msg, "full");
                    Truncate(text, full, Json.Bool(msg, "emitted"));
                    Post(() => TranscriptTruncated?.Invoke(text, full));
                    break;
                }
                case "transcript_revised":
                {
                    string previous = Json.Str(msg, "previous");
                    Revise(text, previous);
                    Post(() => TranscriptRevised?.Invoke(text, previous));
                    break;
                }
                case "tool_call":
                {
                    var call = new ToolCall { Name = Json.Str(msg, "name"), Arguments = Json.Str(msg, "arguments", "{}"), CallId = Json.Str(msg, "call_id") };
                    Post(() => ToolCall?.Invoke(call));
                    break;
                }
                case "session_limit":
                {
                    var d = data as Dictionary<string, object> ?? msg;
                    float seconds = (float)Json.Num(d, "seconds");
                    string reason = Json.Str(d, "reason");
                    Post(() => SessionLimit?.Invoke(seconds, reason));
                    break;
                }
                case "error":
                    RaiseError(LokutorError.FromServer(data));
                    break;
                case "llm_request":
                    ServeLlm(msg);
                    break;
                case "llm_cancel":
                    lock (replies)
                        if (replies.TryGetValue(Json.Str(msg, "request_id"), out var r)) r.Cancel();
                    break;
            }
        }

        void Advance(int gen)
        {
            if (gen <= Generation) return;
            Generation = gen;
            Post(() => GenerationStarted?.Invoke(gen));
        }

        void RaiseStatus(string value) => Post(() => Status?.Invoke(value));

        void RaiseError(LokutorError e) => Post(() => Error?.Invoke(e));

        protected override void OnCallbackFailed(Exception e) => Error?.Invoke(new LokutorError("callback", e.Message, e.GetType().Name, false));

        void Remember(string role, string text)
        {
            lock (messages) messages.Add(new TranscriptLine { Role = role, Text = text, Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        }

        // The player heard only part of an agent line: the history keeps what was heard.
        void Truncate(string heard, string full, bool emitted)
        {
            if (!emitted)
            {
                if (heard.Length > 0) Remember("agent", heard);
                return;
            }
            lock (messages)
            {
                int i = messages.FindLastIndex(m => m.Role == "agent" && m.Text == full);
                if (i < 0) return;
                if (heard.Length == 0) messages.RemoveAt(i);
                else messages[i].Text = heard;
            }
        }

        void Revise(string text, string previous)
        {
            lock (messages)
            {
                int i = messages.FindLastIndex(m => m.Role == "user" && (previous.Length == 0 || m.Text == previous));
                if (i >= 0)
                {
                    messages.RemoveRange(i + 1, messages.Count - i - 1);
                    messages[i].Text = text;
                    return;
                }
            }
            Remember("user", text);
        }

        // ---- Your own model ------------------------------------------------------------------------------

        void ServeLlm(Dictionary<string, object> msg)
        {
            var request = new LlmRequest { RequestId = Json.Str(msg, "request_id") };
            request.Messages.AddRange(Json.Objects(msg, "messages"));
            request.Tools.AddRange(Json.Objects(msg, "tools"));
            var reply = new LlmReply(this, request.RequestId);
            if (OnLlmRequest == null)
            {
                reply.Fail("no OnLlmRequest handler");
                return;
            }
            lock (replies) replies[request.RequestId] = reply;
            Post(async () =>
            {
                try
                {
                    string text = await OnLlmRequest(request, reply);
                    reply.Chunk(text);
                    reply.Done();
                }
                catch (OperationCanceledException) when (reply.Cancelled) { }
                catch (Exception e) { reply.Fail(e.Message); }
                finally { lock (replies) replies.Remove(request.RequestId); }
            });
        }

        void CancelReplies()
        {
            lock (replies)
            {
                foreach (var r in replies.Values) r.Cancel();
                replies.Clear();
            }
        }
    }
}
