using System;
using System.Collections.Generic;
using System.Linq;
using Lokutor;

static class UnitTests
{
    static int passed, failed;

    static void Check(bool ok, string what)
    {
        if (ok) { passed++; return; }
        failed++;
        Console.WriteLine("  FAIL: " + what);
    }

    public static bool RunAll()
    {
        passed = failed = 0;
        Run("MiniJson round trip", MiniJsonRoundTrip);
        Run("Resampler: chunked equals one-shot, length and pitch kept", Resampler);
        Run("PlaybackBuffer: wrap, grow, clear, level", Playback);
        Run("Action tool definition", ToolDefinition);
        Run("Session: replies, interruptions and stale audio", SessionAudio);
        Run("Session: Speaking holds through a reply and ends after it", SessionSpeakingState);
        Run("Session: the mic stays muted until the voice has really been heard", MicMutedThroughEcho);
        Run("Session: transcripts and states reach Update()", SessionEvents);
        Run("Session: an action runs and answers the server", SessionAction);
        Run("Session: unknown action and a throwing handler still answer", SessionActionErrors);
        Run("Session: errors and visemes", SessionErrorsAndVisemes);
        Console.WriteLine($"unit tests: {passed} checks passed, {failed} failed");
        return failed == 0;
    }

    static void Run(string name, Action test)
    {
        int before = failed;
        try { test(); }
        catch (Exception e) { failed++; Console.WriteLine("  EXCEPTION: " + e); }
        Console.WriteLine((failed == before ? "ok   " : "FAIL ") + name);
    }

    static void MiniJsonRoundTrip()
    {
        var obj = new Dictionary<string, object>
        {
            ["s"] = "quote \" backslash \\ newline \n tab \t ñ €",
            ["n"] = 3.25,
            ["i"] = 42,
            ["b"] = true,
            ["null"] = null,
            ["list"] = new List<object> { 1, "two", false },
            ["nested"] = new Dictionary<string, object> { ["k"] = "v" },
        };
        var json = MiniJson.Serialize(obj);
        var back = (Dictionary<string, object>)MiniJson.Parse(json);
        Check((string)back["s"] == (string)obj["s"], "string escapes survive");
        Check((double)back["n"] == 3.25 && (double)back["i"] == 42, "numbers");
        Check((bool)back["b"] && back["null"] == null, "bool and null");
        Check(((List<object>)back["list"]).Count == 3, "list");
        Check((string)((Dictionary<string, object>)back["nested"])["k"] == "v", "nested object");
        var u = (Dictionary<string, object>)MiniJson.Parse("{\"x\":\"caf\\u00e9\", \"e\": -1.5e2 }");
        Check((string)u["x"] == "café" && (double)u["e"] == -150, "\\u escape and exponent");
        bool threw = false;
        try { MiniJson.Parse("{\"a\":1"); } catch (FormatException) { threw = true; }
        Check(threw, "truncated JSON is rejected");
    }

    static void Resampler()
    {
        const int inRate = 48000, outRate = 16000;
        var sine = new float[inRate]; // 1 s of 440 Hz
        for (int i = 0; i < sine.Length; i++) sine[i] = (float)Math.Sin(2 * Math.PI * 440 * i / inRate) * 0.5f;

        var oneShot = new StreamingResampler(inRate, outRate).Process(sine, 0, sine.Length);
        var chunked = new List<float>();
        var r = new StreamingResampler(inRate, outRate);
        var rnd = new Random(1);
        for (int pos = 0; pos < sine.Length;)
        {
            int n = Math.Min(rnd.Next(1, 2000), sine.Length - pos);
            chunked.AddRange(r.Process(sine, pos, n));
            pos += n;
        }
        Check(Math.Abs(oneShot.Length - outRate) <= 2, $"one second becomes {outRate} samples (got {oneShot.Length})");
        Check(Math.Abs(chunked.Count - oneShot.Length) <= 1, $"chunked length {chunked.Count} vs {oneShot.Length}");
        int same = Math.Min(chunked.Count, oneShot.Length);
        double maxDiff = 0;
        for (int i = 0; i < same; i++) maxDiff = Math.Max(maxDiff, Math.Abs(chunked[i] - oneShot[i]));
        Check(maxDiff < 1e-5, "chunk boundaries change nothing (max diff " + maxDiff + ")");
        int crossings = 0;
        for (int i = 1; i < oneShot.Length; i++) if (oneShot[i - 1] < 0 && oneShot[i] >= 0) crossings++;
        Check(Math.Abs(crossings - 440) <= 2, "pitch kept: " + crossings + " cycles in 1 s");

        var up = new StreamingResampler(16000, 44100).Process(oneShot, 0, oneShot.Length);
        Check(Math.Abs(up.Length - 44100) <= 3, "upsampling length " + up.Length);
    }

    static void Playback()
    {
        var b = new PlaybackBuffer(100, initialSeconds: 1); // 100-sample ring
        b.Write(Enumerable.Repeat(0.5f, 80).ToArray());
        var dst = new float[60];
        Check(b.Read(dst, 0, 60) == 60 && dst.All(v => v == 0.5f), "read what was written");
        b.Write(Enumerable.Range(0, 70).Select(i => (float)i / 100).ToArray()); // wraps around
        dst = new float[90];
        int got = b.Read(dst, 0, 90);
        Check(got == 90 && dst[0] == 0.5f && dst[19] == 0.5f && Math.Abs(dst[20] - 0f) < 1e-6 && Math.Abs(dst[89] - 0.69f) < 1e-6,
              "order kept across the wrap");
        b.Write(new float[500]); // grows past the ring
        Check(Math.Abs(b.BufferedSeconds - 5f) < 1e-6, "grows when written faster than read");
        b.Write(Enumerable.Repeat(0.9f, 10).ToArray());
        b.Clear();
        dst = new float[10];
        Check(b.Read(dst, 0, 10) == 0 && dst.All(v => v == 0f) && !b.IsPlaying, "clear empties; reads pad with silence");
        b.Write(Enumerable.Repeat(0.25f, 10).ToArray());
        b.Read(dst, 0, 10);
        Check(b.Level > 0.9f, "level follows loudness: " + b.Level);
    }

    static void ToolDefinition()
    {
        var a = new AgentAction("give_item", "Give an item", args => "ok",
            new ActionParam("item", "Which item", ParamType.String, true, "sword", "shield"),
            new ActionParam("count", "How many", ParamType.Integer, false));
        var def = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Serialize(a.ToToolDefinition()));
        var fn = (Dictionary<string, object>)def["function"];
        var ps = (Dictionary<string, object>)fn["parameters"];
        var props = (Dictionary<string, object>)ps["properties"];
        Check((string)def["type"] == "function" && (string)fn["name"] == "give_item", "function shape");
        Check(((List<object>)((Dictionary<string, object>)props["item"])["enum"]).Count == 2, "choices become enum");
        Check((string)((Dictionary<string, object>)props["count"])["type"] == "integer", "integer type");
        Check(((List<object>)ps["required"]).SequenceEqual(new object[] { "item" }), "only required params listed");
        bool threw = false;
        try { new AgentAction("open door", "x", _ => ""); } catch (ArgumentException) { threw = true; }
        Check(threw, "names with spaces are refused");
    }

    static string AudioFrame(int gen, int samples, short value = 1000)
    {
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++) { bytes[2 * i] = (byte)(value & 0xff); bytes[2 * i + 1] = (byte)(value >> 8); }
        return "{\"type\":\"audio\",\"generation\":" + gen + ",\"data\":\"" + Convert.ToBase64String(bytes) + "\"}";
    }

    static void SessionAudio()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test" });
        s.HandleServerMessage("{\"type\":\"server_rates\",\"playback\":44100,\"input\":16000}");
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":1}");
        s.HandleServerMessage(AudioFrame(1, 4410));
        Check(Math.Abs(s.Playback.BufferedSeconds - 0.1f) < 1e-4, "reply audio queued");
        Check(s.State == AgentState.Speaking, "speaking once audio arrives: " + s.State);
        s.HandleServerMessage(AudioFrame(0, 4410));
        Check(Math.Abs(s.Playback.BufferedSeconds - 0.1f) < 1e-4, "older generation dropped");
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":2}");
        Check(!s.Playback.IsPlaying && s.State == AgentState.Thinking, "a newer reply clears the old one");
        s.HandleServerMessage(AudioFrame(2, 4410));
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"listening\"}");
        Check(s.Playback.IsPlaying, "'listening' alone does not cut the voice (could be a cough)");
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"interrupted\"}");
        Check(!s.Playback.IsPlaying && s.State == AgentState.PlayerTalking, "an interruption cuts it");
        s.HandleServerMessage("{\"type\":\"server_rates\",\"playback\":24000,\"input\":16000}");
        Check(s.PlaybackRate == 24000, "playback rate follows the server");
    }

    static void SessionSpeakingState()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test", EchoTailMs = 600 });
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":1}");
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"speaking\"}");
        s.Update();
        Check(s.State == AgentState.Speaking, "'speaking' before the first audio is not undone by Update(): " + s.State);
        System.Threading.Thread.Sleep(450);
        s.Update();
        Check(s.State == AgentState.Speaking, "nor 450 ms later while the first audio is still on its way: " + s.State);
        s.HandleServerMessage(AudioFrame(1, 4410));
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":1}");
        Check(s.State == AgentState.Speaking, "a same-reply 'thinking' (next sentence) keeps Speaking: " + s.State);
        s.Playback.Read(new float[4410], 0, 4410); // the engine plays it out
        s.Update();
        Check(s.State == AgentState.Speaking, "still Speaking right after the audio ran out (more may follow)");
        System.Threading.Thread.Sleep(450);
        s.Update();
        Check(s.State == AgentState.Speaking, "still Speaking through the echo tail, while the mic is muted: " + s.State);
        System.Threading.Thread.Sleep(300);
        s.Update();
        Check(s.State == AgentState.Ready, "Ready once nothing has arrived and the voice can no longer be heard: " + s.State);
    }

    static void MicMutedThroughEcho()
    {
        // MuteMicWhileSpeaking replaces the microphone with silence; AgentAudible decides when. Checked
        // through the public surface: what SendMicrophone would send is not observable without a
        // socket, so the decision is read back by reflection on the private property.
        var s = new LokutorSession(new SessionOptions { ApiKey = "test", EchoTailMs = 300 });
        var audible = typeof(LokutorSession).GetProperty("AgentAudible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        bool Audible() => (bool)audible.GetValue(s);
        Check(!Audible(), "silent before anything is said");
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":1}");
        s.HandleServerMessage(AudioFrame(1, 4410));
        Check(Audible(), "audible while voice is queued");
        s.Playback.Read(new float[4410], 0, 4410);
        s.SetVoiceAudibleUntil(DateTime.UtcNow.AddMilliseconds(400)); // the engine's buffers still hold it
        System.Threading.Thread.Sleep(350);
        Check(Audible(), "still audible while the engine plays what it took");
        System.Threading.Thread.Sleep(300);
        Check(Audible(), "and through the echo tail after it");
        System.Threading.Thread.Sleep(450);
        Check(!Audible(), "open again once the echo has died down");
    }

    static void SessionEvents()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test" });
        var seen = new List<string>();
        s.PlayerSaid += t => seen.Add("player:" + t);
        s.PlayerSaidPartial += t => seen.Add("partial:" + t);
        s.AgentSaid += t => seen.Add("agent:" + t);
        s.AgentInterrupted += t => seen.Add("cut:" + t);
        s.StateChanged += st => seen.Add("state:" + st);
        s.SessionLimit += sec => seen.Add("limit:" + sec);
        s.HandleServerMessage("{\"type\":\"status\",\"data\":\"connected\",\"session_id\":\"agent_1\"}");
        s.HandleServerMessage("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"hel\",\"partial\":true}");
        s.HandleServerMessage("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"hello\"}");
        s.HandleServerMessage("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Welcome!\"}");
        s.HandleServerMessage("{\"type\":\"transcript_truncated\",\"full\":\"Welcome traveller\",\"data\":\"Welcome\"}");
        s.HandleServerMessage("{\"type\":\"session_limit\",\"data\":{\"seconds\":180,\"reason\":\"demo\"}}");
        Check(seen.Count == 0, "nothing is delivered before Update()");
        s.Update();
        Check(seen.SequenceEqual(new[] { "state:Ready", "partial:hel", "player:hello", "agent:Welcome!", "cut:Welcome", "limit:180" }),
              "delivered in order on Update(): " + string.Join(", ", seen));
        Check(s.SessionId == "agent_1", "session id kept");
    }

    static void SessionAction()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test" });
        string gaveItem = null;
        int threadOfHandler = -1;
        s.AddAction("give_item", "Give the player an item", a =>
        {
            gaveItem = a.GetString("item");
            threadOfHandler = Environment.CurrentManagedThreadId;
            return "Gave " + gaveItem + ".";
        }, new ActionParam("item", "Which item"));
        string calledName = null, calledResult = null;
        s.ActionCalled += (n, a, r) => { calledName = n; calledResult = r; };
        s.HandleServerMessage("{\"type\":\"tool_call\",\"name\":\"give_item\",\"arguments\":\"{\\\"item\\\":\\\"iron sword\\\"}\",\"call_id\":\"call_7\"}");
        Check(gaveItem == null, "the handler waits for Update()");
        s.Update();
        Check(gaveItem == "iron sword" && threadOfHandler == Environment.CurrentManagedThreadId, "handler ran on the Update() thread with its argument");
        Check(calledName == "give_item" && calledResult == "Gave iron sword.", "ActionCalled reports the result");
        var sent = s.DrainOutgoingTextForTests().Select(t => (Dictionary<string, object>)MiniJson.Parse(t)).ToList();
        var result = sent.FirstOrDefault(m => (string)m["type"] == "tool_result");
        Check(result != null, "a tool_result is sent back");
        if (result != null)
        {
            var data = (Dictionary<string, object>)result["data"];
            Check((string)data["call_id"] == "call_7" && (string)data["result"] == "Gave iron sword.", "with the call id and the handler's answer");
        }
    }

    static void SessionActionErrors()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test" });
        s.AddAction("explode", "Always fails", a => throw new InvalidOperationException("no gunpowder"));
        s.HandleServerMessage("{\"type\":\"tool_call\",\"name\":\"fly\",\"arguments\":\"{}\",\"call_id\":\"c1\"}");
        s.HandleServerMessage("{\"type\":\"tool_call\",\"name\":\"explode\",\"arguments\":\"not json\",\"call_id\":\"c2\"}");
        s.Update();
        var results = s.DrainOutgoingTextForTests()
            .Select(t => (Dictionary<string, object>)MiniJson.Parse(t))
            .Where(m => (string)m["type"] == "tool_result")
            .Select(m => (Dictionary<string, object>)m["data"]).ToList();
        Check(results.Count == 2, "both calls answered (else the character waits for its timeout)");
        Check(results.Any(r => (string)r["call_id"] == "c1" && ((string)r["result"]).Contains("no action called fly")), "unknown action explained");
        Check(results.Any(r => (string)r["call_id"] == "c2" && ((string)r["result"]).Contains("no gunpowder")), "handler exception explained");
    }

    static void SessionErrorsAndVisemes()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "test" });
        LokutorError err = null;
        IReadOnlyList<VisemeMark> marks = null;
        s.Error += e => err = e;
        s.VisemesReceived += m => marks = m;
        s.HandleServerMessage("{\"type\":\"error\",\"data\":{\"code\":\"validation.invalid_voice\",\"message\":\"voice \\\"Z9\\\" is not recognised\",\"retryable\":false}}");
        s.HandleServerMessage("{\"type\":\"visemes\",\"data\":[{\"v\":0,\"c\":\"H\",\"t\":0.0},{\"v\":1,\"c\":\"o\",\"t\":0.08},{\"v\":2,\"c\":\"m\",\"t\":0.15}]}");
        s.Update();
        Check(err != null && err.Code == "validation.invalid_voice", "server error code reaches Error");
        Check(marks != null && marks.Count == 3 && marks[1].Shape == MouthShape.O && marks[2].Shape == MouthShape.MBP && Math.Abs(marks[1].Time - 0.08f) < 1e-6,
              "visemes mapped to mouth shapes with times");
    }
}
