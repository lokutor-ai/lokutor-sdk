// A scripted conversation with a game character, driven exactly the way an engine drives the SDK:
// a 20 ms "frame" loop that feeds the microphone, pulls the character's voice from the playback
// buffer, and calls Update(). The player's lines are spoken by macOS `say` and streamed as if from a
// microphone, in real time, with silence in between. Everything the player would hear goes to a WAV.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lokutor;

static class NpcDemo
{
    const int MixRate = 44100;
    const int FrameMs = 20;

    static readonly string[] PlayerLines =
    {
        "Hi! I'm heading into the mountains. What do you have for me?",
        "The iron sword sounds good. I'll take it.",
        "Thanks, Brom. Goodbye!",
    };

    public static async Task<bool> Run(string outDir)
    {
        string key = Environment.GetEnvironmentVariable("LOKUTOR_API_KEY");
        if (string.IsNullOrEmpty(key)) { Console.WriteLine("demo: set LOKUTOR_API_KEY"); return false; }
        Directory.CreateDirectory(outDir);

        // ---- The game's side: a shop, a purse, an inventory ----
        int gold = 30;
        var prices = new Dictionary<string, int> { ["iron sword"] = 20, ["steel shield"] = 25, ["healing potion"] = 5 };
        var inventory = new List<string>();
        var log = new StringBuilder();
        var clock = Stopwatch.StartNew();
        void Log(string line)
        {
            string stamped = $"[{clock.Elapsed.TotalSeconds,6:0.00}s] {line}";
            Console.WriteLine(stamped);
            log.AppendLine(stamped);
        }

        var session = new LokutorSession(new SessionOptions
        {
            ApiKey = key,
            Voice = "M1",
            Language = "en",
            Persona =
                "You are Brom, the blacksmith of Oakvale, a gruff but kind dwarf. Speak in one or two short sentences. " +
                "Your shop sells: iron sword (20 gold), steel shield (25 gold), healing potion (5 gold). Nothing else. " +
                "When the player agrees to buy something, call give_item; it takes the gold. Greet the player when they arrive.",
        });

        // ---- The SDK part a developer writes: one action ----
        session.AddAction("give_item", "Sell an item to the player: takes the gold and puts the item in their bag.", args =>
        {
            string item = args.GetString("item");
            if (!prices.TryGetValue(item, out int price)) return "There is no " + item + " in the shop.";
            if (gold < price) return $"The player has only {gold} gold; the {item} costs {price}.";
            gold -= price;
            inventory.Add(item);
            return $"Gave the {item}. The player paid {price} gold and has {gold} left.";
        }, new ActionParam("item", "The item to sell", ParamType.String, true, prices.Keys.ToArray()));

        var turns = new List<(string who, string text)>();
        var actions = new List<string>();
        bool everSpoke = false;
        double lastSpeechStart = -1;
        session.StateChanged += st =>
        {
            Log("state: " + st + (st == AgentState.Ready && session.SessionId != null ? " (" + session.SessionId + ")" : ""));
            if (st == AgentState.Speaking) { everSpoke = true; lastSpeechStart = clock.Elapsed.TotalSeconds; }
        };
        session.PlayerSaid += t => { Log("PLAYER (recognised): " + t); turns.Add(("player", t)); };
        session.AgentSaid += t => { Log("BROM: " + t); turns.Add(("brom", t)); };
        session.ActionCalled += (name, a, result) =>
        {
            Log($"ACTION {name}({a.Json}) -> {result}");
            actions.Add(name + " " + a.GetString("item"));
        };
        session.Error += e => Log("ERROR " + e);
        session.Disconnected += r => Log("disconnected: " + r);

        // ---- Audio: the player's lines as 16 kHz mic audio ----
        var lines = PlayerLines.Select(SayToPcm16k).ToList();

        var playerTrack = new List<float>();
        var agentTrack = new List<float>();
        var micQueue = new Queue<float>();
        var micUp = new StreamingResampler(16000, MixRate);
        var micFrame = new float[16000 * FrameMs / 1000];
        var speakerFrame = new float[MixRate * FrameMs / 1000];
        long frameIndex = 0;
        var frameClock = Stopwatch.StartNew();

        // One engine frame: microphone in, speaker out, Update().
        void Frame()
        {
            for (int i = 0; i < micFrame.Length; i++) micFrame[i] = micQueue.Count > 0 ? micQueue.Dequeue() : 0f;
            session.SendMicrophone(micFrame, 0, micFrame.Length, 16000);
            playerTrack.AddRange(micUp.Process(micFrame, 0, micFrame.Length));
            if (session.PlaybackRate == MixRate) session.Playback.Read(speakerFrame, 0, speakerFrame.Length);
            else
            {
                var raw = new float[session.PlaybackRate * FrameMs / 1000];
                session.Playback.Read(raw, 0, raw.Length);
                var up = new StreamingResampler(session.PlaybackRate, MixRate).Process(raw, 0, raw.Length);
                Array.Clear(speakerFrame, 0, speakerFrame.Length);
                Array.Copy(up, speakerFrame, Math.Min(up.Length, speakerFrame.Length));
            }
            agentTrack.AddRange(speakerFrame);
            session.Update();
            frameIndex++;
            long due = frameIndex * FrameMs - frameClock.ElapsedMilliseconds;
            if (due > 0) Thread.Sleep((int)due);
        }

        void RunFor(double seconds, Func<bool> until = null)
        {
            var end = clock.Elapsed.TotalSeconds + seconds;
            while (clock.Elapsed.TotalSeconds < end && (until == null || !until())) Frame();
        }

        // Waits for the character to answer and finish (or gives up after `timeout` seconds).
        double WaitForReply(double timeout, double sinceSeconds)
        {
            RunFor(timeout, () => lastSpeechStart > sinceSeconds && session.State == AgentState.Ready);
            RunFor(0.6); // the room after the voice
            return lastSpeechStart > sinceSeconds ? lastSpeechStart - sinceSeconds : -1;
        }

        Log("connecting...");
        try { await session.ConnectAsync(); }
        catch (Exception e) { Log("connect failed: " + e.Message); return false; }

        var latencies = new List<double>();
        Log("waiting for Brom to greet the player");
        double greet = WaitForReply(15, 0);
        Log(greet >= 0 ? "greeting played" : "no greeting within 15 s");

        for (int i = 0; i < lines.Count; i++)
        {
            Log("PLAYER (speaking): " + PlayerLines[i]);
            foreach (var v in lines[i]) micQueue.Enqueue(v);
            RunFor(30, () => micQueue.Count == 0);
            double spokeEnd = clock.Elapsed.TotalSeconds;
            double latency = WaitForReply(20, spokeEnd);
            if (latency >= 0) { latencies.Add(latency); Log($"Brom answered {latency * 1000:0} ms after the player stopped"); }
            else Log("no answer within 20 s");
        }
        await session.DisconnectAsync();
        session.Update();

        // ---- Results ----
        var mix = new float[Math.Max(playerTrack.Count, agentTrack.Count)];
        for (int i = 0; i < mix.Length; i++)
        {
            float p = i < playerTrack.Count ? playerTrack[i] : 0f, a = i < agentTrack.Count ? agentTrack[i] : 0f;
            mix[i] = Math.Max(-1f, Math.Min(1f, p + a));
        }
        string wav = Path.Combine(outDir, "brom_conversation.wav");
        WriteWav(wav, mix, MixRate);

        bool heardAgent = agentTrack.Any(v => Math.Abs(v) > 0.01f);
        int recognised = turns.Count(t => t.who == "player");
        bool sold = actions.Any(a => a.StartsWith("give_item iron sword"));
        var summary = new StringBuilder();
        summary.AppendLine("# Talking to Brom the blacksmith (Lokutor game SDK, C# core)");
        summary.AppendLine();
        summary.AppendLine($"- Character voice heard: {(heardAgent ? "yes" : "NO")}");
        summary.AppendLine($"- Player lines recognised: {recognised} of {PlayerLines.Length}");
        summary.AppendLine($"- Game action: {(sold ? "give_item(iron sword) ran" : "give_item did NOT run")}; inventory [{string.Join(", ", inventory)}], gold {gold}");
        if (latencies.Count > 0)
            summary.AppendLine($"- Reply started {string.Join(", ", latencies.Select(l => (l * 1000).ToString("0") + " ms"))} after the player stopped talking (median {(latencies.OrderBy(x => x).ElementAt(latencies.Count / 2) * 1000):0} ms, includes end-of-turn detection)");
        summary.AppendLine($"- Audio: {Path.GetFileName(wav)} ({mix.Length / (double)MixRate:0.0} s, what the player hears and says)");
        summary.AppendLine();
        summary.AppendLine("## Transcript");
        foreach (var t in turns) summary.AppendLine($"- **{(t.who == "player" ? "Player" : "Brom")}:** {t.text}");
        summary.AppendLine();
        summary.AppendLine("## Log");
        summary.AppendLine("```");
        summary.Append(log);
        summary.AppendLine("```");
        File.WriteAllText(Path.Combine(outDir, "brom_conversation.md"), summary.ToString());
        Console.WriteLine();
        Console.WriteLine(summary.ToString().Split(new[] { "## Transcript" }, StringSplitOptions.None)[0]);

        return everSpoke && heardAgent && recognised >= 2 && sold;
    }

    /// <summary>Speaks a line with macOS `say` and returns it as 16 kHz mono floats.</summary>
    static float[] SayToPcm16k(string text)
    {
        string aiff = Path.Combine(Path.GetTempPath(), "lokutor_line_" + Guid.NewGuid().ToString("N") + ".aiff");
        string raw = Path.ChangeExtension(aiff, ".raw");
        try
        {
            Exec("say", "-v", "Samantha", "-o", aiff, text);
            Exec("ffmpeg", "-loglevel", "error", "-y", "-i", aiff, "-ac", "1", "-ar", "16000", "-f", "s16le", raw);
            return Pcm16.ToFloat(File.ReadAllBytes(raw));
        }
        finally
        {
            if (File.Exists(aiff)) File.Delete(aiff);
            if (File.Exists(raw)) File.Delete(raw);
        }
    }

    static void Exec(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        string err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception(file + " failed: " + err);
    }

    public static void WriteWav(string path, float[] samples, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = samples.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataBytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataBytes);
        w.Write(Pcm16.FromFloat(samples, 0, samples.Length));
    }
}
