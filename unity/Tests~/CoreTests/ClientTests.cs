// The clients, against the mock server.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lokutor;

static partial class UnitTests
{
    static MockServer server;

    static void RunClientTests()
    {
        using (server = new MockServer())
        {
            Run("Helpers: languages, tags, visemes, origins, audio", Helpers);
            Run("Discovery: voices, errors", Discovery);
            Run("Errors: codes and retryable", Errors);
            Run("VoiceAgentClient: configuration", ClientConfig);
            Run("VoiceAgentClient: messages", ClientMessages);
            Run("VoiceAgentClient: transcript", ClientTranscript);
            Run("VoiceAgentClient: a session", ClientSocket);
            Run("VoiceAgentClient: opens a dropped connection again", ClientReconnect);
            Run("VoiceAgentClient: stops while reconnecting, and starts afresh", ClientReconnectStopped);
            Run("VoiceAgentClient: refused", ClientRefused);
            Run("VoiceAgentClient: refused, and why", ClientRefusedWhy);
            Run("VoiceAgentClient: your own model", ClientOwnModel);
            Run("VoiceAgentClient: managed audio", ClientManaged);
            Run("TTSClient", Tts);
            Run("TTSClient: errors", TtsErrors);
            Run("TTSClient: cancel", TtsCancel);
            Run("TTSClient: everything on the calling thread", TtsContext);
            Run("STTClient", Stt);
            Run("SpeechToTextClient", SttStream);
        }
    }

    // ---- Helpers for the tests -----------------------------------------------------------------

    static void Run(string name, Func<Task> test) => Run(name, () =>
    {
        if (server != null)
        {
            server.Refuse = false; server.RefuseWith = "";
            server.OnMessage = null;
            lock (server.Sessions) server.Sessions.Clear();
        }
        test().GetAwaiter().GetResult();
    });

    static void Eq<T>(T got, T want, string what) => Check(Equals(got, want), what + ": got " + got + ", wanted " + want);

    static void Same(IEnumerable<string> got, IEnumerable<string> want, string what)
    {
        string a = string.Join(" | ", got), b = string.Join(" | ", want);
        Check(a == b, what + ": got " + a + ", wanted " + b);
    }

    static void Near(double got, double want, double tolerance, string what) => Check(Math.Abs(got - want) <= tolerance, what + ": got " + got + ", wanted " + want);

    /// <summary>Waits until something holds, pumping a game's frames.</summary>
    static async Task<bool> Until(Func<bool> condition, Action frame = null, double seconds = 4)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            frame?.Invoke();
            if (condition()) return true;
            if (DateTime.UtcNow > end) return false;
            await Task.Delay(5);
        }
    }

    static async Task Pause(double seconds, Action frame = null)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            frame?.Invoke();
            await Task.Delay(5);
        }
    }

    static byte[] Tone(double seconds, double volume = 0.3)
    {
        var f = new float[(int)(seconds * LokutorApi.OutputRate)];
        for (int i = 0; i < f.Length; i++) f[i] = (float)(Math.Sin(i * 2 * Math.PI * 220 / LokutorApi.OutputRate) * volume);
        return Pcm16.FromFloat(f, 0, f.Length);
    }

    static Dictionary<string, object> Obj(object o) => (Dictionary<string, object>)o;

    static VoiceAgentClient NewClient() => new VoiceAgentClient("sk_test", "You are Mira.") { ServerUrl = server.Url("/ws/agent") };

    static Task<bool> Configured(Action frame = null) => Until(() => server.Current != null && server.Current.Last("rates") != null, frame);

    // ---- Tests -----------------------------------------------------------------------------------

    static void Helpers()
    {
        Eq(LokutorApi.LanguageToWire("auto"), "", "auto follows the player");
        Eq(LokutorApi.LanguageToWire("es"), "es", "a language is itself");
        Eq(LokutorApi.StripTags("Okay. [laugh] That's it! [Sigh]"), "Okay. That's it!", "sound tags");
        Eq(LokutorApi.StripTags("[sad] I'm sorry."), "I'm sorry.", "emotion tags");
        Eq(LokutorApi.StripTags("Take the [iron sword]."), "Take the [iron sword].", "other brackets stay");
        Eq(LokutorApi.VisemeId("pp"), 1, "viseme names ignore case");
        Eq(LokutorApi.VisemeId("what"), 0, "unknown viseme");
        Eq(LokutorApi.HttpOrigin("wss://api.lokutor.com/ws/agent"), "https://api.lokutor.com", "origin");
        Eq(LokutorApi.HttpOrigin("ws://localhost:8080/ws/agent"), "http://localhost:8080", "local origin");
        Eq(LokutorApi.Visemes.Count, 15, "15 visemes");
        Same(LokutorApi.ReconnectableCloseCodes.OrderBy(c => c).Select(c => c.ToString()), new[] { "1001", "1006", "1011", "1012", "1013", "1014" }, "reconnectable codes");
        var pcm = LokutorAudio.Float32ToPcm16(new[] { 0f, 0.5f, -0.5f, 2f });
        var back = LokutorAudio.Pcm16ToFloat32(pcm);
        Near(back[1], 0.5, 0.001, "PCM round trip");
        Near(back[3], 1, 0.001, "PCM clips");
        Near(LokutorAudio.CalculateRMS(new[] { 0.5f, -0.5f }), 0.5, 0.001, "rms");
        Near(LokutorAudio.NormalizeAudio(new[] { 0.1f, -0.2f })[1], -0.95, 0.001, "normalise");
        var wav = LokutorAudio.WavBytes(pcm, 16000);
        Eq(wav.Length, 44 + pcm.Length, "WAV size");
        Eq(BitConverter.ToInt32(wav, 24), 16000, "WAV rate");
        Eq(LokutorAudio.Resample(new float[4800], 48000, 16000).Length, 1600, "resample");
    }

    static async Task Discovery()
    {
        server.HttpStatus = 200;
        server.HttpBody = "{\"voices\":[{\"id\":\"F1\",\"name\":\"Vega\",\"gender\":\"female\",\"languages\":[\"en\",\"es\"]}]}";
        var voices = await LokutorApi.GetVoicesAsync(server.Url("/ws/agent"));
        Check(voices.Count == 1 && voices[0].Id == "F1" && voices[0].Name == "Vega" && voices[0].Languages.Count == 2, "GetVoicesAsync");
        Check(server.HttpRequests.Last().Line.StartsWith("GET /voices "), "asks the API's root, not the socket's path");
        server.HttpStatus = 503;
        server.HttpBody = "{\"code\":\"internal.error\",\"message\":\"Busy\"}";
        LokutorError error = null;
        try { await LokutorApi.GetModelsAsync(server.Url("/ws/agent")); }
        catch (LokutorException e) { error = e.Error; }
        Check(error != null && error.Code == "internal.error" && error.Message == "Busy", "a failed request throws the API's error");
    }

    static void Errors()
    {
        var e = LokutorError.FromServer(MiniJson.Parse("{\"code\":\"billing.insufficient_balance\",\"message\":\"No\",\"detail\":\"d\"}"));
        Check(e.Code == "billing.insufficient_balance" && e.Message == "No" && e.Detail == "d", "code, message, detail");
        Check(!e.Retryable, "billing is not retryable");
        Eq(LokutorError.FromServer("AI response service error.").Code, "internal.error", "plain string errors");
        Check(new LokutorError("internal.timeout", "t").Retryable, "timeouts are");
        Check(!LokutorError.FromServer(MiniJson.Parse("{\"code\":\"internal.error\",\"message\":\"\",\"retryable\":false}")).Retryable, "the server's word wins");
        Eq(LokutorError.FromServer(null).Code, "internal.error", "nothing at all");
    }

    static List<string> Types(VoiceAgentClient c) => c.ConfigMessages().Select(m => (string)m["type"]).ToList();
    static Dictionary<string, object> Config(VoiceAgentClient c, string type) => c.ConfigMessages().First(m => (string)m["type"] == type);

    static void ClientConfig()
    {
        var c = new VoiceAgentClient("k", "Hi", "M2", "auto") { WantVisemes = true };
        c.Tools.Add(new ToolDefinition("f", "does f"));
        Same(Types(c), new[] { "voice", "language", "tools", "visemes", "prompt", "rates" }, "order, rates last");
        Eq((string)Config(c, "language")["data"], "", "auto is sent empty");
        Eq((bool)Config(c, "visemes")["data"], true, "visemes");
        Check((int)Config(c, "rates")["playback"] == 44100 && (int)Config(c, "rates")["input"] == 16000, "rates");
        c = new VoiceAgentClient("k") { AgentId = "a1" };
        Same(Types(c), new[] { "visemes", "rates" }, "a dashboard agent keeps its own voice, language and prompt");
        c.UpdateVoice("F3");
        Same(Types(c), new[] { "voice", "visemes", "rates" }, "unless set");
        c = new VoiceAgentClient("k", "p") { Pitch = 2, Llm = new CustomLLMConfig { BaseUrl = "https://x/v1", Model = "m" } };
        Eq(MiniJson.Serialize(Config(c, "llm")["data"]), "{\"base_url\":\"https://x/v1\",\"model\":\"m\"}", "own endpoint");
        Eq((float)Config(c, "pitch")["data"], 2f, "pitch");
        Same(new[] { (string)Config(c, "voice")["data"], (string)Config(c, "language")["data"] }, new[] { "F1", "en" }, "defaults");
        c.Llm = null;
        c.OnLlmRequest = (request, reply) => Task.FromResult("hi");
        Eq((string)Obj(Config(c, "llm")["data"])["mode"], "client", "own model in the game");
    }

    static void ClientMessages()
    {
        var c = new VoiceAgentClient();
        var audio = new List<string>();
        var visemes = new List<IReadOnlyList<Viseme>>();
        var errors = new List<string>();
        var calls = new List<string>();
        var partial = new List<string>();
        var gens = new List<string>();
        string limit = "";
        c.Audio += (pcm, g) => audio.Add(pcm.Length + "@" + g);
        c.Visemes += (l, g) => visemes.Add(l);
        c.Error += e => errors.Add(e.Code);
        c.ToolCall += t => calls.Add(t.Name + " " + t.Arguments + " " + t.CallId);
        c.TranscriptionPartial += t => partial.Add(t);
        c.GenerationStarted += g => gens.Add(g.ToString());
        c.SessionLimit += (s, r) => limit = s + " " + r;
        c.HandleMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":2}");
        c.HandleMessage("{\"type\":\"audio\",\"data\":\"AAAAAA==\",\"generation\":1}");
        c.HandleMessage("{\"type\":\"audio\",\"data\":\"AAAAAA==\",\"generation\":2}");
        c.HandleMessage("{\"type\":\"visemes\",\"generation\":1,\"data\":[{\"v\":1,\"c\":\"PP\",\"t\":0,\"d\":0.1}]}");
        c.HandleMessage("{\"type\":\"visemes\",\"generation\":2,\"data\":[{\"v\":10,\"c\":\"aa\",\"t\":0.1,\"d\":0.2},{\"v\":0,\"c\":\"sil\",\"t\":0.3}]}");
        c.HandleMessage("{\"type\":\"visemes\",\"generation\":2,\"data\":[{\"v\":99,\"c\":\"oh\",\"t\":0.4,\"d\":0.1}]}");
        c.HandleMessage("{\"type\":\"audio\",\"data\":\"AAAAAA==\",\"generation\":3}");
        c.HandleMessage("{\"type\":\"error\",\"data\":{\"code\":\"validation.invalid_voice\",\"message\":\"No such voice\"}}");
        c.HandleMessage("{\"type\":\"error\",\"data\":\"AI response service error.\"}");
        c.HandleMessage("{\"type\":\"tool_call\",\"name\":\"open\",\"arguments\":\"{\\\"x\\\":1}\",\"call_id\":\"c1\"}");
        c.HandleMessage("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"hel\",\"partial\":true}");
        c.HandleMessage("{\"type\":\"session_limit\",\"data\":{\"seconds\":90,\"reason\":\"demo\"}}");
        c.HandleMessage("{\"type\":\"server_rates\",\"playback\":48000,\"input\":16000}");
        c.HandleMessage("[1,2]");
        c.HandleMessage("not json");
        Check(audio.Count == 0, "nothing is raised before Update()");
        c.Update();
        Same(audio, new[] { "4@2", "4@3" }, "stale audio is dropped");
        Eq(visemes.Count, 2, "stale visemes are dropped");
        Check(visemes[0][0].V == 10 && visemes[0][0].C == "aa" && visemes[0][0].T == 0.1f && visemes[0][0].D == 0.2f, "viseme");
        Check(visemes[0][1].D > 0f, "a missing duration gets one");
        Eq(visemes[1][0].V, 13, "an id out of range is read from its name");
        Same(gens, new[] { "2", "3" }, "a newer reply announces itself");
        Same(errors, new[] { "validation.invalid_voice", "internal.error" }, "both error shapes");
        Same(calls, new[] { "open {\"x\":1} c1" }, "tool call");
        Same(partial, new[] { "hel" }, "partial");
        Eq(limit, "90 demo", "session limit");
        Eq(c.PlaybackRate, 48000, "server rates");
    }

    static void ClientTranscript()
    {
        var c = new VoiceAgentClient();
        const string full = "Welcome to my shop, take a look around.";
        void Line(string role, string text) => c.HandleMessage("{\"type\":\"transcript\",\"role\":\"" + role + "\",\"data\":\"" + text + "\"}");
        Line("user", "Hello");
        Line("agent", full);
        c.HandleMessage("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"par\",\"partial\":true}");
        Eq(c.GetTranscriptText(), "You: Hello\nAgent: " + full, "history, without partials");
        c.HandleMessage("{\"type\":\"transcript_truncated\",\"role\":\"agent\",\"data\":\"Welcome to…\",\"full\":\"" + full + "\",\"emitted\":true}");
        Eq(c.GetTranscript()[1].Text, "Welcome to…", "an interrupted line keeps what was heard");
        Line("agent", "Never heard");
        c.HandleMessage("{\"type\":\"transcript_truncated\",\"role\":\"agent\",\"data\":\"\",\"full\":\"Never heard\",\"emitted\":true}");
        Eq(c.GetTranscript().Count, 2, "a line never heard is withdrawn");
        c.HandleMessage("{\"type\":\"transcript_truncated\",\"role\":\"agent\",\"data\":\"One mo…\",\"full\":\"One moment\",\"emitted\":false}");
        Eq(c.GetTranscript()[2].Text, "One mo…", "a line cut before its transcript is added");
        Line("user", "I want");
        Line("agent", "Yes?");
        c.HandleMessage("{\"type\":\"transcript_revised\",\"role\":\"user\",\"data\":\"I want a sword\",\"previous\":\"I want\"}");
        var last = c.GetTranscript().Last();
        Eq(last.Text, "I want a sword", "a paused sentence becomes one line");
        Eq(last.Role, "user", "and the agent line after it is dropped");
        c.GetTranscript().Clear();
        Check(c.GetTranscript().Count > 0, "GetTranscript is a copy");
        c.Reset();
        Eq(c.GetTranscript().Count, 0, "reset");
    }

    static async Task ClientSocket()
    {
        var c = NewClient();
        c.WantVisemes = true;
        var log = new List<string>();
        c.Status += s => log.Add(s);
        c.Ended += (code, reason) => log.Add("ended " + code + " " + reason);
        c.Error += e => log.Add("error " + e.Code);
        Check(await c.ConnectAsync() && c.Connected, "connects");
        await Configured();
        var s1 = server.Current;
        Same(s1.Types(), new[] { "voice", "language", "visemes", "prompt", "rates" }, "the server receives the configuration");
        Eq(s1.Headers["X-API-Key"], "sk_test", "the key is in a header");
        Check(!s1.Url.Contains("api_key"), "and not in the URL");
        s1.Send("{\"type\":\"status\",\"data\":\"connected\",\"session_id\":\"s-1\"}");
        await Until(() => c.SessionId == "s-1");
        Eq(c.SessionId, "s-1", "session id");
        c.SendAudio(new byte[] { 1, 0, 2, 0 });
        c.SendAudioFloat(new[] { 0f, 0.1f, 0.2f }, 16000);
        await Until(() => s1.AudioBytes >= 10);
        Eq(s1.AudioBytes, 10, "microphone audio arrives");
        c.UpdatePrompt("You are angry.");
        c.UpdateVoice("M3");
        c.UpdateLanguage("auto");
        c.UpdateTools(new ToolDefinition[0]);
        c.UpdatePitch(3);
        c.UpdateVisemes(false);
        c.SubmitToolResult("c9", new Dictionary<string, object> { ["ok"] = true });
        await Until(() => s1.Last("tool_result") != null);
        Eq((string)s1.Data("prompt"), "You are angry.", "prompt update");
        Eq((string)s1.Data("voice"), "M3", "voice update");
        Eq((string)s1.Data("language"), "", "language update");
        Eq((double)s1.Data("pitch"), 3.0, "pitch update");
        Eq((bool)s1.Data("visemes"), false, "visemes update");
        Eq(MiniJson.Serialize(s1.Data("tool_result")), "{\"call_id\":\"c9\",\"result\":\"{\\\"ok\\\":true}\"}", "tool result");
        s1.Close(1000, "goodbye");
        await Until(() => log.Contains("disconnected"), c.Update);
        Check(log.Contains("ended 1000 goodbye"), "a deliberate end is reported");
        Check(log.Contains("error session.ended"), "with its error, as in JavaScript");
        await Pause(0.3, c.Update);
        Check(!log.Contains("reconnecting"), "and not opened again");

        c.Auth = () => Task.FromResult("wst_abc");
        Check(await c.ConnectAsync(), "connects again");
        await Until(() => server.Count == 2);
        Check(server.Current.Url.Contains("api_key=wst_abc"), "a ticket is in the query");
        c.Auth = () => Task.FromResult("");
        c.Disconnect();
        log.Clear();
        Check(!await c.ConnectAsync(), "no credential, no connection");
        c.Update();
        Check(log.Contains("error auth.missing_key"), "and it says why");
        Eq(c.LastError.Code, "auth.missing_key", "also in LastError");
        c.Auth = () => throw new InvalidOperationException("my server is down");
        Check(!await c.ConnectAsync() && c.LastError.Detail.Contains("my server is down"), "an auth callback that throws");

        c = NewClient();
        c.AgentId = "agent 1";
        await c.ConnectAsync();
        await Until(() => server.Count == 3);
        Check(server.Current.Url.EndsWith("?agent_id=agent%201"), "a dashboard agent: " + server.Current.Url);
        c.Dispose();
    }

    static async Task ClientReconnect()
    {
        var c = NewClient();
        var log = new List<string>();
        int auths = 0, authThread = 0;
        c.Auth = () =>
        {
            auths++;
            authThread = Environment.CurrentManagedThreadId;
            return Task.FromResult("wst_" + auths);
        };
        c.Status += s => log.Add(s);
        c.Reconnected += () => log.Add("reconnected");
        await c.ConnectAsync();
        await Configured();
        server.Current.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":7}");
        await Until(() => c.Generation == 7);
        server.Current.Drop();
        await Until(() => log.Contains("reconnecting"), c.Update);
        Check(log.Contains("reconnecting"), "a dropped connection is noticed");
        int frameThread = 0;
        await Until(() => log.Contains("reconnected"), () =>
        {
            frameThread = Environment.CurrentManagedThreadId;
            c.Update();
            if (auths == 2 && authThread != frameThread) authThread = -1;
            else if (auths == 2) auths = 3;
        }, 5);
        Check(log.Contains("reconnected"), "and opened again");
        Eq(auths, 3, "with a ticket asked for during Update()");
        Check(server.Current.Url.Contains("wst_2"), "a new one");
        Eq(c.Generation, 0, "the new session counts its replies from the start");
        await Configured();
        Same(server.Current.Types(), new[] { "voice", "language", "visemes", "prompt", "rates" }, "with the whole configuration");
        c.Disconnect();
        c.Update();
        Eq(log.Last(), "disconnected", "disconnect");
    }

    static async Task ClientReconnectStopped()
    {
        var c = NewClient();
        var log = new List<string>();
        c.Status += s => log.Add(s);
        c.Reconnected += () => log.Add("reconnected");
        await c.ConnectAsync();
        await Configured();
        server.Current.Drop();
        await Until(() => log.Contains("reconnecting"), c.Update);
        c.Disconnect();
        c.Update();
        Eq(log.Last(), "disconnected", "stopped");
        Check(await c.ConnectAsync(), "a new connection");
        await Pause(2.3, c.Update);
        Eq(server.Count, 2, "and only that one");
        Check(!log.Contains("reconnected"), "which is not a reconnection");
        c.Disconnect();

        c = NewClient();
        c.AutoReconnect = false;
        log.Clear();
        c.Status += s => log.Add(s);
        c.Error += e => log.Add(e.Code);
        await c.ConnectAsync();
        await Until(() => server.Count == 3);
        server.Current.Drop();
        await Until(() => log.Contains("disconnected"), c.Update);
        Same(log, new[] { "ws.close", "disconnected" }, "AutoReconnect off");
    }

    static async Task ClientRefused()
    {
        server.Refuse = true;
        var c = NewClient();
        var log = new List<string>();
        c.Error += e => log.Add(e.Code);
        c.Status += s => log.Add(s);
        Check(!await c.ConnectAsync(), "ConnectAsync is false");
        c.Update();
        Same(log, new[] { "ws.close", "disconnected" }, "one error");
        Check(c.LastError.Retryable, "which may work later");
        await Pause(0.2, c.Update);
        Eq(log.Count, 2, "a first connection that fails is not retried");
    }

    // The API says why over HTTP, and so do the clients.
    static async Task ClientRefusedWhy()
    {
        server.Refuse = true;
        server.RefuseWith = "{\"code\":\"auth.invalid_key\",\"message\":\"The provided API key is invalid or has been revoked.\",\"retryable\":false}";
        var c = NewClient();
        var log = new List<string>();
        c.Error += e => log.Add(e.Code);
        c.Status += s => log.Add(s);
        Check(!await c.ConnectAsync(), "a wrong key does not connect");
        c.Update();
        Same(log, new[] { "auth.invalid_key", "disconnected" }, "and says why, once");
        Check(!c.LastError.Retryable && c.LastError.Message.Contains("revoked"), "in the server's words");

        server.Refuse = false;
        log.Clear();
        Check(await c.ConnectAsync(), "with the right one it connects");
        await Configured();
        int n = server.Count;
        server.RefuseWith = "{\"code\":\"billing.insufficient_balance\",\"message\":\"The balance is empty.\",\"retryable\":false}";
        server.Refuse = true;
        server.Current.Drop();
        await Until(() => log.Contains("billing.insufficient_balance"), c.Update);
        await Pause(2.5, c.Update);
        Eq(server.Count, n, "a reconnection that no attempt can fix is not attempted again");
        Eq(log.Last(), "disconnected", "it ends");

        var tts = new TTSClient("sk_test") { ServerUrl = server.Url("/ws/tts") };
        var r = await tts.SynthesizeAsync("Hello.");
        Check(!r.Ok && r.Error.Code == "billing.insufficient_balance", "text to speech says why");
    }

    static async Task ClientOwnModel()
    {
        var c = NewClient();
        c.LlmTools = "none";
        c.OnLlmRequest = async (request, reply) =>
        {
            reply.Chunk("You said ");
            await Task.Yield();
            reply.Chunk((string)request.Messages.Last()["content"]);
            reply.ToolCall("wave", new Dictionary<string, object> { ["hand"] = "left" });
            return null;
        };
        await c.ConnectAsync();
        await Configured();
        var s = server.Current;
        Eq((string)Obj(s.Data("llm"))["tools"], "none", "without the agent's tools");
        s.Send("{\"type\":\"llm_request\",\"request_id\":\"r1\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"tools\":[]}");
        await Until(() => s.Last("llm_done") != null, c.Update);
        Same(s.Types().Skip(s.Types().Count - 4), new[] { "llm_chunk", "llm_chunk", "llm_tool_call", "llm_done" }, "streams the reply");
        Eq((string)s.Last("llm_tool_call")["arguments"], "{\"hand\":\"left\"}", "tool call");
        Eq((string)s.Last("llm_done")["request_id"], "r1", "request id");
        bool cancelled = false;
        c.OnLlmRequest = async (request, reply) =>
        {
            try { await Task.Delay(5000, reply.Token); }
            catch (OperationCanceledException)
            {
                cancelled = reply.Cancelled;
                throw;
            }
            return "late";
        };
        s.Send("{\"type\":\"llm_request\",\"request_id\":\"r2\",\"messages\":[],\"tools\":[]}");
        await Pause(0.1, c.Update);
        s.Send("{\"type\":\"llm_cancel\",\"request_id\":\"r2\"}");
        await Until(() => cancelled, c.Update);
        Check(cancelled, "the player speaking over it cancels it");
        c.OnLlmRequest = (request, reply) => throw new InvalidOperationException("out of memory");
        s.Send("{\"type\":\"llm_request\",\"request_id\":\"r3\",\"messages\":[],\"tools\":[]}");
        await Until(() => s.Last("llm_error") != null, c.Update);
        Eq((string)s.Last("llm_error")["data"], "out of memory", "a handler that throws is answered");
        Eq(s.Types().Count(t => t == "llm_done"), 1, "and nothing is sent for the cancelled one");
        c.Dispose();
    }

    sealed class FakeAudio : IAudioManager
    {
        public Action<float[], int, int> OnAudio;
        public readonly List<string> Log = new List<string>();
        public bool HasMicrophone = true;
        public bool Muted { get; set; }
        public float GetAmplitude() => 0.5f;
        public void PlayAudio(byte[] pcm16, int sampleRate) => Log.Add("play " + pcm16.Length + "@" + sampleRate);
        public void StopPlayback() => Log.Add("stop");
        public void StopMicrophone() => Log.Add("mic off");

        public bool StartMicrophone(Action<float[], int, int> onAudio)
        {
            OnAudio = onAudio;
            return HasMicrophone;
        }
    }

    static async Task ClientManaged()
    {
        var c = NewClient();
        var audio = new FakeAudio();
        Check(await c.StartManagedAsync(audio), "starts");
        await Configured();
        var s = server.Current;
        audio.OnAudio(new float[960], 960, 48000);
        await Until(() => s.AudioBytes > 0);
        Near(s.AudioBytes, 640, 4, "the microphone is resampled and sent");
        s.Audio16(new byte[8], 1);
        await Until(() => audio.Log.Contains("play 8@44100"), c.Update);
        s.Send("{\"type\":\"status\",\"data\":\"interrupted\"}");
        await Until(() => audio.Log.Last() == "stop", c.Update);
        Same(audio.Log, new[] { "stop", "play 8@44100", "stop" }, "the voice plays, and stops when interrupted");
        Check(c.ToggleMute() && audio.Muted && !c.ToggleMute(), "mute");
        Eq(c.GetAmplitude(), 0.5f, "amplitude");
        c.Disconnect();
        c.Update();
        Check(audio.Log.Contains("mic off"), "disconnecting releases the microphone");
        var errors = new List<string>();
        c.Error += e => errors.Add(e.Code);
        Check(!await c.StartManagedAsync(new FakeAudio { HasMicrophone = false }), "without a microphone it does not start");
        c.Update();
        Same(errors, new[] { "audio.microphone_unavailable" }, "and says why");
    }

    // ---- Text to speech ----------------------------------------------------------------------------

    static void ServeTts(MockServer.Session s, Dictionary<string, object> msg)
    {
        if (!msg.TryGetValue("text", out var text)) return;
        switch ((string)text)
        {
            case "bad":
                s.Send("{\"type\":\"error\",\"data\":{\"code\":\"validation.invalid_voice\",\"message\":\"No such voice\"}}");
                return;
            case "broken":
                s.Send("{\"type\":\"error\",\"data\":{\"code\":\"tts.synthesis_failed\",\"message\":\"Failed\",\"retryable\":true}}");
                Thread.Sleep(30);
                s.Send("EOS");
                return;
            case "silent":
                return;
        }
        if (Equals(msg["visemes"], true)) s.Send("{\"type\":\"visemes\",\"data\":[{\"v\":10,\"c\":\"aa\",\"t\":0,\"d\":0.4}]}");
        s.Send(Tone(0.2));
        if ((string)text == "cut") { s.Drop(); return; }
        s.Send(Tone(0.2));
        s.Send("EOS");
    }

    static TTSClient NewTts()
    {
        server.OnMessage = ServeTts;
        return new TTSClient("sk_test") { ServerUrl = server.Url("/ws/tts") };
    }

    static async Task Tts()
    {
        var tts = NewTts();
        int chunks = 0, ttfb = -1, visemes = 0;
        var r = await tts.SynthesizeAsync("Hello", new SynthesizeOptions
        {
            Voice = "M2", Visemes = true, OnAudio = p => Interlocked.Increment(ref chunks), OnTTFB = ms => ttfb = ms, OnVisemes = l => visemes += l.Count,
        });
        Check(r.Ok, "ok");
        Near(r.Seconds, 0.4, 0.001, "all the audio");
        Check(r.Visemes.Count == 1 && visemes == 1, "visemes");
        Eq(chunks, 2, "streams");
        Check(ttfb >= 0, "time to first byte");
        Same(server.Current.Text[0].Keys.OrderBy(k => k, StringComparer.Ordinal), new[] { "language", "text", "visemes", "voice" }, "sends only what is set");
        var one = tts.SynthesizeAsync("One", new SynthesizeOptions { Speed = 1.1f, Steps = 8, Pronunciations = new Dictionary<string, string> { ["Brom"] = "brohm" } });
        var two = tts.SynthesizeAsync("Two");
        await Task.WhenAll(one, two);
        Check(one.Result.Ok && two.Result.Ok, "requests queue");
        Eq(server.Count, 1, "one connection");
        Same(server.Current.Text.Select(m => (string)m["text"]), new[] { "Hello", "One", "Two" }, "in order");
        Check(Equals(server.Current.Text[1]["steps"], 8.0) && (string)Obj(server.Current.Text[1]["pronunciations"])["Brom"] == "brohm", "what is set is sent");
        tts.Dispose();
    }

    static async Task TtsErrors()
    {
        var tts = NewTts();
        string told = null;
        var r = await tts.SynthesizeAsync("bad", new SynthesizeOptions { OnError = e => told = e.Code });
        Check(!r.Ok && r.Error.Code == "validation.invalid_voice", "a refused request");
        await Until(() => told != null);
        Eq(told, "validation.invalid_voice", "also in OnError");
        r = await tts.SynthesizeAsync("broken");
        Check(!r.Ok && r.Error.Code == "tts.synthesis_failed" && r.Error.Retryable, "a failed synthesis, followed by EOS");
        r = await tts.SynthesizeAsync("fine");
        Check(r.Ok && r.Audio.Length > 0, "does not end the next one");
        r = await tts.SynthesizeAsync("cut");
        Check(!r.Ok && r.Error.Code == "internal.error" && r.Audio.Length > 0, "a connection lost half way");
        tts.FirstAudioTimeout = TimeSpan.FromMilliseconds(200);
        r = await tts.SynthesizeAsync("silent");
        Check(!r.Ok && r.Error.Code == "internal.timeout", "a server that does not answer");
        tts.Close();
        server.Refuse = true;
        r = await tts.SynthesizeAsync("nobody");
        Check(!r.Ok && r.Error.Code == "ws.close", "a server that is down");
        tts.ApiKey = null;
        tts.Auth = () => Task.FromResult<string>(null);
        r = await tts.SynthesizeAsync("nobody");
        Eq(r.Error.Code, "auth.missing_key", "no credential");
    }

    static async Task TtsCancel()
    {
        var tts = NewTts();
        var first = tts.SynthesizeAsync("silent");
        var second = tts.SynthesizeAsync("waiting");
        await Until(() => server.Current != null && server.Current.Text.Count == 1);
        tts.Cancel();
        var all = await Task.WhenAll(first, second);
        Check(all.All(r => !r.Ok && r.Error.Code == "internal.cancelled"), "the one being spoken and the one waiting");
        var one = new CancellationTokenSource();
        first = tts.SynthesizeAsync("silent", null, one.Token);
        second = tts.SynthesizeAsync("fine");
        await Until(() => server.Count == 2);
        one.Cancel();
        Check((await first).Error.Code == "internal.cancelled" && (await second).Ok, "or only one of them");
        tts.KeepOpen = TimeSpan.FromMilliseconds(100);
        await tts.SynthesizeAsync("fine");
        var last = server.Current;
        await Until(() => last.Ws.State != System.Net.WebSockets.WebSocketState.Open);
        Check(last.Ws.State != System.Net.WebSockets.WebSocketState.Open, "the connection is let go of when idle");
    }

    /// <summary>A game's main thread, as Unity has it.</summary>
    sealed class MainThread : SynchronizationContext
    {
        readonly System.Collections.Concurrent.ConcurrentQueue<Action> posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public override void Post(SendOrPostCallback d, object state) => posted.Enqueue(() => d(state));

        public void RunUntil(Func<bool> done, Action frame = null, double seconds = 5)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (!done() && DateTime.UtcNow < end)
            {
                while (posted.TryDequeue(out var a)) a();
                frame?.Invoke();
                Thread.Sleep(5);
            }
        }
    }

    static void TtsContext()
    {
        lock (server.Sessions) server.Sessions.Clear();
        var main = new MainThread();
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(main);
        try
        {
            int thread = Environment.CurrentManagedThreadId, elsewhere = 0, callbacks = 0;
            void Here()
            {
                callbacks++;
                if (Environment.CurrentManagedThreadId != thread) elsewhere++;
            }
            var tts = NewTts();
            tts.Auth = () =>
            {
                Here();
                return Task.FromResult("wst_1");
            };
            var options = new SynthesizeOptions { Visemes = true, OnAudio = p => Here(), OnVisemes = l => Here(), OnTTFB = ms => Here(), OnError = e => Here() };
            var a = tts.SynthesizeAsync("bad", options);
            var b = tts.SynthesizeAsync("fine", options);       // opens a second connection, from a thread of the pool
            main.RunUntil(() => a.IsCompleted && b.IsCompleted && callbacks >= 7);
            Check(b.IsCompleted && b.Result.Ok, "spoken");
            Check(callbacks == 7 && elsewhere == 0, "auth (2), error, first audio, audio (2), visemes: " + callbacks + " callbacks, " + elsewhere + " elsewhere");
            tts.Dispose();
        }
        finally { SynchronizationContext.SetSynchronizationContext(before); }
    }

    // ---- Speech to text ----------------------------------------------------------------------------

    static async Task Stt()
    {
        server.HttpStatus = 200;
        server.HttpBody = "{\"text\":\"hello there\",\"latency_ms\":41.5,\"engine\":\"conv-2.0\",\"sample_rate\":16000,\"language\":\"en\",\"duration_seconds\":1.2,\"segments\":[{\"text\":\"hello\",\"start\":0,\"end\":0.5,\"duration\":0.5}]}";
        var stt = new STTClient("sk_test") { ServerUrl = server.Url("/ws/stt") };
        var r = await stt.TranscribeAsync(new byte[] { 0, 0, 1, 0 }, new TranscribeOptions { SampleRate = 16000, Language = "en", Keyterms = new List<string> { "Brom" } });
        Check(r.Ok && r.Text == "hello there" && r.Engine == "conv-2.0" && r.DurationSeconds == 1.2f && r.Segments.Count == 1, "transcribes");
        var req = server.HttpRequests.Last();
        Check(req.Line.StartsWith("POST /stt/transcribe "), "route");
        Eq(req.Headers["X-API-Key"], "sk_test", "key");
        Eq((string)req.Body["format"], "pcm16", "format");
        Same(((List<object>)req.Body["keyterms"]).Select(k => (string)k), new[] { "Brom" }, "keyterms");
        await stt.TranscribeAsync(LokutorAudio.WavBytes(new byte[320], 16000));
        Eq((string)server.HttpRequests.Last().Body["format"], "wav", "a WAV is recognised");
        server.HttpStatus = 402;
        server.HttpBody = "{\"code\":\"billing.insufficient_balance\",\"message\":\"Top up\"}";
        r = await stt.TranscribeAsync(new byte[2]);
        Check(!r.Ok && r.Error.Code == "billing.insufficient_balance", "a refused request says why");
        stt.Auth = () => throw new InvalidOperationException("down");
        r = await stt.TranscribeAsync(new byte[2]);
        Check(!r.Ok && r.Error.Code == "auth.missing_key", "an auth callback that throws");
    }

    static async Task SttStream()
    {
        var stt = new SpeechToTextClient("sk_test") { ServerUrl = server.Url("/ws/stt"), Keyterms = new List<string> { "Brom" } };
        var log = new List<string>();
        stt.PartialTranscript += t => log.Add("p " + t);
        stt.FinalTranscript += t => log.Add("f " + t);
        stt.StatusChange += t => log.Add(t);
        stt.Error += e => log.Add(e.Code);
        Check(await stt.ConnectAsync() && stt.Connected, "connects");
        await Until(() => server.Current != null && server.Current.Text.Count == 1);
        var s = server.Current;
        Eq(MiniJson.Serialize(s.Text[0]), "{\"language\":\"en\",\"vad\":\"silero\",\"keyterms\":[\"Brom\"]}", "first message");
        stt.SendAudio(new byte[] { 1, 0 });
        stt.EndUtterance();
        await Until(() => s.Last("end") != null && s.AudioBytes == 2);
        Eq(s.AudioBytes, 2, "audio");
        s.Send("{\"type\":\"transcript\",\"data\":\"open the\",\"isFinal\":false}");
        s.Send("{\"type\":\"transcript\",\"data\":\"open the gate\",\"isFinal\":true}");
        await Until(() => log.Contains("f open the gate"), stt.Update);
        Same(log, new[] { "connecting", "connected", "p open the", "f open the gate" }, "transcripts");
        stt.Disconnect();
        stt.Update();
        Eq(log.Last(), "disconnected", "disconnect");
        var audio = new FakeAudio();
        Check(await stt.ConnectAsync(audio), "with a microphone");
        await Until(() => server.Count == 2);
        audio.OnAudio(new float[480], 480, 48000);
        await Until(() => server.Current.AudioBytes > 0);
        Check(server.Current.AudioBytes > 300, "which is sent");
        server.Current.Close(1008, "balance");
        await Until(() => log.Last() == "disconnected" && log.Contains("ws.close"), stt.Update);
        Check(log.Contains("ws.close") && audio.Log.Contains("mic off"), "an ended session stops listening");
    }
}
