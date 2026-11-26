// Short live checks against production: the parts of the protocol the demo does not exercise.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lokutor;

static class LiveTests
{
    public static async Task<bool> RunAll()
    {
        string key = Environment.GetEnvironmentVariable("LOKUTOR_API_KEY");
        if (string.IsNullOrEmpty(key)) { Console.WriteLine("live: set LOKUTOR_API_KEY"); return false; }
        bool ok = true;
        ok &= await Check("a ticket from /ws-ticket connects, and the key never leaves the backend", () => TicketConnects(key));
        ok &= await Check("a wrong key fails ConnectAsync with a clear error", WrongKeyFails);
        ok &= await Check("an unknown voice is reported through Error", () => BadVoiceReported(key));
        return ok;
    }

    // What needs no key: the public endpoints, and how a key that is not one is refused.
    public static async Task<bool> RunPublic()
    {
        bool ok = true;
        ok &= await Check("the service is ready", async () => { var h = await LokutorApi.GetHealthAsync(); return h.Ready ? null : h.Status; });
        ok &= await Check("the default voices exist", async () =>
        {
            var v = await LokutorApi.GetVoicesAsync();
            return v.Exists(x => x.Id == "F1") && v.Exists(x => x.Id == "M1") && v.TrueForAll(x => x.Name != "" && x.Languages != null) ? null : v.Count + " voices";
        });
        ok &= await Check("languages", async () => { var l = await LokutorApi.GetLanguagesAsync(); return l.Exists(x => x.Code == "en") && l.Exists(x => x.Code == "es") ? null : l.Count.ToString(); });
        ok &= await Check("a default model", async () => (await LokutorApi.GetModelsAsync()).Exists(m => m.IsDefault) ? null : "none");
        ok &= await Check("the sample rate is the one the SDK plays", async () => { var c = await LokutorApi.GetConfigAsync(); return c.SampleRate == LokutorApi.OutputRate ? null : c.SampleRate.ToString(); });
        ok &= await Check("status", async () => (await LokutorApi.GetStatusAsync()).Count > 0 ? null : "empty");
        ok &= await Check("agent: a wrong key fails ConnectAsync with a clear error", WrongKeyFails);
        ok &= await Check("agent client: a wrong key is an error that is not worth another try", async () =>
        {
            var a = new VoiceAgentClient { ApiKey = "lk_not_a_real_key", AutoReconnect = false };
            LokutorError err = null;
            a.Error += e => err = e;
            bool opened = await a.ConnectAsync();
            var sw = Stopwatch.StartNew();
            while (err == null && sw.ElapsedMilliseconds < 5000) { a.Update(); Thread.Sleep(20); }
            await a.DisconnectAsync();
            Console.WriteLine("       " + err);
            return !opened && err != null && !err.Retryable ? null : "opened " + opened + ", " + err;
        });
        ok &= await Check("text to speech: a wrong key", async () =>
        {
            using var tts = new TTSClient("lk_not_a_real_key");
            var r = await tts.SynthesizeAsync("Hello.");
            Console.WriteLine("       " + r.Error);
            return !r.Ok && r.Audio.Length == 0 && !r.Error.Retryable ? null : "" + r.Error;
        });
        ok &= await Check("speech to text: a wrong key", async () =>
        {
            var r = await new STTClient("lk_not_a_real_key").TranscribeAsync(new byte[4], new TranscribeOptions { SampleRate = 16000 });
            Console.WriteLine("       " + r.Error);
            return !r.Ok && !r.Error.Retryable ? null : "" + r.Error;
        });
        return ok;
    }

    static async Task<bool> Check(string name, Func<Task<string>> test)
    {
        string failure;
        try { failure = await test(); }
        catch (Exception e) { failure = e.GetType().Name + ": " + e.Message; }
        Console.WriteLine((failure == null ? "ok   " : "FAIL ") + name + (failure == null ? "" : " — " + failure));
        return failure == null;
    }

    // What a game's backend does: trade the API key for a ticket that expires.
    static async Task<string> MintTicket(string key)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.lokutor.com/ws-ticket");
        req.Headers.Add("Authorization", "Bearer " + key);
        var resp = await http.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new IOException("ws-ticket HTTP " + (int)resp.StatusCode + ": " + body);
        var d = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(body);
        return (string)d["ticket"];
    }

    static async Task<string> TicketConnects(string key)
    {
        var s = new LokutorSession(new SessionOptions { TicketProvider = () => MintTicket(key), Persona = "Say only: hello.", Voice = "F1", Language = "en" });
        await s.ConnectAsync();
        var sw = Stopwatch.StartNew();
        while (s.State != AgentState.Ready && s.State != AgentState.Speaking && sw.ElapsedMilliseconds < 8000) { s.Update(); Thread.Sleep(20); }
        var reached = s.State;
        await s.DisconnectAsync();
        return reached == AgentState.Ready || reached == AgentState.Speaking ? null : "state " + reached;
    }

    static async Task<string> WrongKeyFails()
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = "lk_not_a_real_key" });
        try { await s.ConnectAsync(); }
        catch (IOException e) { return s.State == AgentState.Disconnected ? null : "state " + s.State + " after " + e.Message; }
        var sw = Stopwatch.StartNew();
        string reason = null;
        s.Disconnected += r => reason = r;
        while (reason == null && sw.ElapsedMilliseconds < 5000) { s.Update(); Thread.Sleep(20); }
        await s.DisconnectAsync();
        return reason != null ? null : "connected with a fake key";
    }

    static async Task<string> BadVoiceReported(string key)
    {
        var s = new LokutorSession(new SessionOptions { ApiKey = key, Voice = "Z9_not_a_voice", Language = "en", Persona = "Say only: hello." });
        LokutorError err = null;
        s.Error += e => err = e;
        await s.ConnectAsync();
        var sw = Stopwatch.StartNew();
        while (err == null && sw.ElapsedMilliseconds < 6000) { s.Update(); Thread.Sleep(20); }
        await s.DisconnectAsync();
        return err != null && err.Code.Contains("voice") ? null : "error was " + (err?.ToString() ?? "none");
    }
}
