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
