// The voice's clock, lip sync, the session of a character, and written lines.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lokutor;

static partial class UnitTests
{
    static void RunCharacterTests()
    {
        Run("PlaybackBuffer: the clock of what is heard", PlaybackClock);
        Run("LipSync: rigs", LipRigs);
        Run("LipSync: timeline", LipTimeline);
        Run("LipSync: loudness, stepped, sprites", LipFallback);
        using (server = new MockServer())
        {
            Run("Session: a conversation", AgentConversation);
            Run("Session: actions", AgentActionsLive);
            Run("Session: interrupted", AgentInterrupted);
            Run("Session: the microphone is muted while the character speaks", AgentMute);
            Run("Session: what it would say while an action runs", AgentFiller);
            Run("Session: a background sound is not a reply", AgentBackground);
            Run("Session: context", AgentContext);
            Run("Session: voice, language, pitch, transcript", AgentUpdates);
            Run("Session: reconnecting", AgentReconnect);
            Run("Session: the end", AgentEnd);
            Run("VoiceLines: kept", LinesKept);
            Run("VoiceLines: queue", LinesQueue);
        }
    }

    // ---- The voice's clock ------------------------------------------------------------------------

    static void PlaybackClock()
    {
        var b = new PlaybackBuffer(1000);
        var block = new float[100];
        Check(!b.IsAudible && b.HeardSeconds == 0f && b.HeardLevel == 0f, "silent at first");
        b.Write(Enumerable.Repeat(0.5f, 300).ToArray());
        b.Read(block, 0, 100);
        b.MarkPlayed(0);
        Check(b.HeardSeconds == 0f && b.IsAudible, "read ahead is not heard yet");
        b.MarkPlayed(50);
        Near(b.HeardSeconds, 0.05, 1e-6, "heard as far as it has come out");
        Near(b.HeardLevel, 0.5, 0.01, "at its loudness");
        b.Read(block, 0, 100);
        b.Read(block, 0, 100);
        b.Read(block, 0, 100);          // silence
        b.MarkPlayed(350);
        Near(b.HeardSeconds, 0.3, 1e-6, "silence after the voice does not count");
        Check(!b.IsAudible && b.HeardLevel == 0f, "and the voice is over");
        b.Write(new float[100]);
        b.Read(block, 0, 100);
        b.MarkPlayed(400);
        Near(b.HeardSeconds, 0.3, 1e-6, "a gap in the voice");
        b.MarkPlayed(450);
        Near(b.HeardSeconds, 0.35, 1e-6, "is skipped");
        b.MarkPlayed(420);
        Near(b.HeardSeconds, 0.35, 1e-6, "the clock never runs backwards");
        int clears = b.Clears;
        b.Clear();
        Check(b.HeardSeconds == 0f && !b.IsAudible && b.Clears == clears + 1, "a new reply counts from 0");
        b.Write(Enumerable.Repeat(0.1f, 100).ToArray());
        b.Read(block, 0, 100);
        b.MarkPlayed(b.TotalRead);
        Near(b.HeardSeconds, 0.1, 1e-6, "on the same timeline of the sound card");
    }

    // ---- Lip sync ---------------------------------------------------------------------------------

    static float[] Face(MouthRig rig, int shapes, LipSync lips)
    {
        var f = new float[shapes];
        foreach (var s in rig.Shapes) f[s.Index] = s.Weight(lips.Weights, lips.Intensity);
        return f;
    }

    static void LipRigs()
    {
        var rigs = new (string Name, MouthRigKind Kind, string[] Shapes, string Open, string Round)[]
        {
            ("Oculus", MouthRigKind.Visemes, LokutorApi.Visemes.Select(v => "viseme_" + v).ToArray(), "viseme_aa", "viseme_ou"),
            ("Ready Player Me", MouthRigKind.Visemes, "viseme_sil viseme_PP viseme_FF viseme_TH viseme_DD viseme_kk viseme_CH viseme_SS viseme_nn viseme_RR viseme_aa viseme_E viseme_I viseme_O viseme_U".Split(' '), "viseme_aa", "viseme_U"),
            ("VRChat", MouthRigKind.Visemes, "vrc.v_sil vrc.v_pp vrc.v_ff vrc.v_th vrc.v_dd vrc.v_kk vrc.v_ch vrc.v_ss vrc.v_nn vrc.v_rr vrc.v_aa vrc.v_e vrc.v_ih vrc.v_oh vrc.v_ou".Split(' '), "vrc.v_aa", "vrc.v_ou"),
            ("an FBX with its node's name", MouthRigKind.Visemes, LokutorApi.Visemes.Select(v => "Head.viseme_" + v).ToArray(), "Head.viseme_aa", "Head.viseme_ou"),
            ("VRM", MouthRigKind.Vrm, new[] { "blink", "aa", "ih", "ou", "ee", "oh" }, "aa", "ou"),
            ("VRoid", MouthRigKind.Vrm, "Face.Fcl_ALL_Joy Face.Fcl_MTH_A Face.Fcl_MTH_I Face.Fcl_MTH_U Face.Fcl_MTH_E Face.Fcl_MTH_O".Split(' '), "Face.Fcl_MTH_A", "Face.Fcl_MTH_U"),
            ("ARKit", MouthRigKind.ArKit, "jawOpen mouthFunnel mouthPucker mouthSmileLeft mouthSmileRight mouthClose".Split(' '), "jawOpen", "mouthPucker"),
            ("one shape", MouthRigKind.MouthOpen, new[] { "blink", "mouthOpen" }, "mouthOpen", "mouthOpen"),
        };
        foreach (var r in rigs)
        {
            var rig = MouthRig.Find(r.Shapes);
            Eq(rig.Kind, r.Kind, r.Name + " is recognised");
            var lips = new LipSync();
            lips.PushVisemes(new[] { new Viseme(10, 0f, 1f) });
            for (int i = 0; i < 30; i++) lips.Advance(0.4f, 0.3f, true, 1f / 60f);
            int open = Array.IndexOf(r.Shapes, r.Open), round = Array.IndexOf(r.Shapes, r.Round);
            Check(Face(rig, r.Shapes.Length, lips)[open] > 0.5f, r.Name + ": aa opens the mouth");
            lips.Clear();
            lips.PushVisemes(new[] { new Viseme(14, 0f, 1f) });
            for (int i = 0; i < 30; i++) lips.Advance(0.4f, 0.3f, true, 1f / 60f);
            Check(Face(rig, r.Shapes.Length, lips)[round] > 0.2f, r.Name + ": ou rounds it");
            lips.Clear();
            for (int i = 0; i < 60; i++) lips.Advance(0f, 0f, false, 1f / 60f);
            Check(Face(rig, r.Shapes.Length, lips)[open] < 0.02f, r.Name + ": and silence closes it");
        }
        var custom = MouthRig.Find(new[] { "smile", "gape" }, new Dictionary<string, Dictionary<string, float>> { ["aa"] = new Dictionary<string, float> { ["gape"] = 0.8f } });
        Check(custom.Kind == MouthRigKind.Custom && custom.Shapes.Count == 1 && custom.Shapes[0].Index == 1 && custom.Shapes[0].PerViseme[10] == 0.8f, "your own names");
        Check(MouthRig.Find(new[] { "smile", "blink" }).Kind == MouthRigKind.None, "a face without a mouth");
    }

    static void LipTimeline()
    {
        var lips = new LipSync { Lead = 0f, Loudness = 0f };
        lips.PushVisemes(new[] { new Viseme(1, 0f, 0.1f), new Viseme(10, 0.1f, 0.2f), new Viseme(14, 0.3f, 0.2f), new Viseme(0, 0.5f, 0.3f) });
        var seen = new List<string>();
        lips.VisemeChanged += v => seen.Add(v);
        var at = new Dictionary<int, (float[] W, float Open)>();
        for (int i = 0; i < 90; i++)
        {
            float t = i / 120f;
            lips.Advance(t, 0.2f, true, 1f / 120f);
            at[(int)Math.Round(t * 100)] = ((float[])lips.Weights.Clone(), lips.MouthOpen);
        }
        Same(seen, new[] { "PP", "aa", "ou", "sil" }, "each shape in order");
        Check(at[5].W[1] > 0.7f && at[5].Open < 0.1f, "p closes the lips");
        Check(at[20].W[10] > 0.9f, "aa is open in its middle");
        Check(at[30].W[10] > 0.2f && at[30].W[14] > 0.2f, "neighbours overlap at the boundary");
        Check(at[40].W[14] > 0.9f, "ou is round in its middle");
        Check(at[74].Open < 0.02f, "sil closes");
        lips.Clear();
        lips.PushVisemes(new[] { new Viseme(10, 0f, 0.2f), new Viseme(4, 0.2f, 0.02f), new Viseme(10, 0.22f, 0.2f) });
        float peak = 0f;
        for (float t = 0f; t < 0.42f; t += 1f / 120f)
        {
            lips.Advance(t, 0.2f, true, 1f / 120f);
            peak = Math.Max(peak, lips.Weights[4]);
        }
        Check(peak < 0.6f, "a 20 ms tongue tap is only hinted (" + peak + ")");
        lips = new LipSync { Loudness = 0f };
        lips.PushVisemes(new[] { new Viseme(10, 0f, 1f), new Viseme(14, 0f, 1f) });
        for (int i = 0; i < 60; i++) lips.Advance(0.5f, 0.2f, true, 1f / 60f);
        Near(lips.Weights[10] + lips.Weights[14], 1, 0.02, "two shapes at once share the mouth");
        lips = new LipSync();
        lips.PushVisemes(new[] { new Viseme(10, 0f, 5f) });
        for (int i = 0; i < 60; i++) lips.Advance(1f, 0.01f, true, 1f / 60f);
        float quiet = lips.MouthOpen;
        for (int i = 0; i < 60; i++) lips.Advance(1f, 0.3f, true, 1f / 60f);
        Check(quiet < lips.MouthOpen - 0.2f, "quiet speech opens less (" + quiet + " and " + lips.MouthOpen + ")");
    }

    static void LipFallback()
    {
        var lips = new LipSync();
        for (int i = 0; i < 30; i++) lips.Advance(0.1f, 0.25f, true, 1f / 60f);
        Check(lips.MouthOpen > 0.6f, "without visemes the mouth follows the loudness");
        Eq(lips.SpriteFrame(4), 2, "an open drawing");
        lips.PushVisemes(new[] { new Viseme(13, 0f, 1f) });
        for (int i = 0; i < 30; i++) lips.Advance(0.5f, 0.25f, true, 1f / 60f);
        Eq(lips.SpriteFrame(4), 3, "the round drawing");
        Eq(lips.SpriteFrame(2), 1, "of two drawings, the open one");
        var stepped = new LipSync { SteppedFps = 12f };
        int steps = 0;
        for (int i = 0; i < 60; i++)
            if (stepped.Advance(0f, 0.2f, true, 1f / 60f)) steps++;
        Check(steps >= 11 && steps <= 12, "on twos: " + steps + " poses in a second");
    }

    // ---- A character ---------------------------------------------------------------------------------

    /// <summary>A game: a frame now and then, a sound card that plays in real time, a mouth.</summary>
    sealed class Stage
    {
        public readonly LokutorSession S;
        public readonly LipSync Lips = new LipSync();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;

        public Stage(SessionOptions o = null)
        {
            o = o ?? new SessionOptions();
            o.ApiKey = "sk_test";
            o.Url = server.Url("/ws/agent");
            S = new LokutorSession(o);
            S.VoiceCleared += Lips.Clear;
            S.Visemes += l => Lips.PushVisemes(l);
        }

        public void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)(now - last);
            var block = new float[(int)(now * S.PlaybackRate) - (int)(last * S.PlaybackRate)];
            last = now;
            S.Update();
            S.Playback.Read(block, 0, block.Length);
            Lips.Advance(S.HeardSeconds, S.Playback.HeardLevel, S.IsSpeaking, delta);
        }

        /// <summary>Starts a conversation and returns the server's side of it, connected.</summary>
        public async Task<MockServer.Session> Talk()
        {
            int before = server.Count;
            await S.ConnectAsync();
            await Until(() => server.Count > before && server.Current.Last("rates") != null, Frame);
            server.Current.Send("{\"type\":\"status\",\"data\":\"connected\",\"session_id\":\"x\"}");
            await Until(() => S.State == AgentState.Ready, Frame);
            return server.Current;
        }

        public Task<bool> State(AgentState state, double seconds = 4) => Until(() => S.State == state, Frame, seconds);
    }

    static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    static void Reply(MockServer.Session s, int gen, string text, double seconds)
    {
        s.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":" + gen + "}");
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"" + text + "\"}");
        s.Send("{\"type\":\"status\",\"data\":\"speaking\",\"generation\":" + gen + "}");
        s.Send("{\"type\":\"visemes\",\"generation\":" + gen + ",\"data\":[{\"v\":1,\"c\":\"PP\",\"t\":0,\"d\":0.1},{\"v\":10,\"c\":\"aa\",\"t\":0.1,\"d\":" + F(seconds - 0.15) +
               "},{\"v\":0,\"c\":\"sil\",\"t\":" + F(seconds - 0.05) + ",\"d\":0.05}]}");
        s.Audio16(Tone(seconds), gen);
    }

    static async Task AgentConversation()
    {
        var g = new Stage(new SessionOptions { Persona = "You are Brom.", Voice = "M1" });
        var states = new List<string>();
        var said = new List<string>();
        var heard = new List<string>();
        g.S.StateChanged += s => states.Add(s.ToString());
        g.S.AgentSaid += t => said.Add(t);
        g.S.PlayerSaid += t => heard.Add(t);
        var s1 = await g.Talk();
        Eq((bool)s1.Data("visemes"), true, "asks for visemes");
        Eq((string)s1.Data("voice"), "M1", "voice");
        Eq((string)s1.Data("prompt"), "You are Brom.", "persona");
        Reply(s1, 1, "Welcome! [laugh]", 0.5);
        await Until(() => g.S.State == AgentState.Speaking && g.Lips.MouthOpen > 0.3f, g.Frame);
        Check(g.Lips.MouthOpen > 0.3f, "the mouth opens with the voice (" + g.Lips.MouthOpen + ")");
        Check(g.S.HeardSeconds > 0.05f && g.S.Playback.HeardLevel > 0.05f && g.S.IsSpeaking, "clock and loudness");
        await g.State(AgentState.Ready);
        Same(states, new[] { "Connecting", "Ready", "Thinking", "Speaking", "Ready" }, "states");
        Same(said, new[] { "Welcome! [laugh]" }, "said once");
        Near(g.S.HeardSeconds, 0.5, 0.01, "all of it heard");
        await Pause(0.1, g.Frame);
        Check(g.Lips.MouthOpen < 0.15f && !g.S.IsSpeaking, "and closes after it");
        s1.Send("{\"type\":\"status\",\"data\":\"listening\"}");
        s1.Send("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"A sword please\"}");
        await Until(() => heard.Count == 1, g.Frame);
        Eq(g.S.State, AgentState.PlayerTalking, "hears the player");
        s1.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":2}");
        s1.Send("{\"type\":\"status\",\"data\":\"speaking\",\"generation\":2}");
        s1.Audio16(Tone(0.5), 2);
        await g.State(AgentState.Speaking);
        s1.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":2}");
        await Pause(0.1, g.Frame);
        Eq(g.S.State, AgentState.Speaking, "a second sentence of the same reply does not stop it");
        g.S.SetPersona("You are angry.");
        await Until(() => (string)s1.Data("prompt") == "You are angry.", g.Frame);
        Eq((string)s1.Data("prompt"), "You are angry.", "SetPersona");
        g.S.Dispose();
    }

    static async Task AgentActionsLive()
    {
        var g = new Stage();
        var called = new List<string>();
        g.S.AddActionAsync("give_item", "Sell an item", async args =>
        {
            called.Add(args.GetString("item"));
            await Task.Delay(50);
            return "Gave the " + args.GetString("item") + ".";
        }, new ActionParam("item", "Which", ParamType.String, true, "sword"), new ActionParam("note", "Anything else", ParamType.String, false));
        var s = await g.Talk();
        var fn = Obj(Obj(((List<object>)s.Data("tools"))[0])["function"]);
        Eq((string)fn["name"], "give_item", "tool definition");
        Same(((List<object>)Obj(fn["parameters"])["required"]).Select(r => (string)r), new[] { "item" }, "optional arguments are not required");
        s.Send("{\"type\":\"tool_call\",\"name\":\"give_item\",\"arguments\":\"{\\\"item\\\":\\\"sword\\\"}\",\"call_id\":\"c1\"}");
        await Until(() => s.Last("tool_result") != null, g.Frame);
        Same(called, new[] { "sword" }, "the handler runs once");
        Eq(MiniJson.Serialize(s.Data("tool_result")), "{\"call_id\":\"c1\",\"result\":\"Gave the sword.\"}", "and an awaited result goes back");
        Eq(g.S.State, AgentState.Thinking, "it thinks until it has used the result");
        s.Send("{\"type\":\"tool_call\",\"name\":\"fly\",\"arguments\":\"{}\",\"call_id\":\"c2\"}");
        await Until(() => (string)Obj(s.Data("tool_result"))["call_id"] == "c2", g.Frame);
        Check(((string)Obj(s.Data("tool_result"))["result"]).Contains("no action called fly"), "an unknown action is answered too");
        g.S.AddAction("wave", "Wave", a => null);
        await Until(() => ((List<object>)s.Data("tools")).Count == 2, g.Frame);
        Eq(((List<object>)s.Data("tools")).Count, 2, "actions added mid-conversation are sent");
        g.S.RemoveAction("wave");
        await Until(() => ((List<object>)s.Data("tools")).Count == 1, g.Frame);
        Eq(((List<object>)s.Data("tools")).Count, 1, "and removed");
        g.S.Dispose();
    }

    static async Task AgentInterrupted()
    {
        var g = new Stage();
        var cut = new List<string>();
        g.S.AgentInterrupted += t => cut.Add(t);
        var s = await g.Talk();
        Reply(s, 1, "A long story about dragons", 5);
        await Until(() => g.S.State == AgentState.Speaking && g.S.HeardSeconds > 0.2f, g.Frame);
        s.Send("{\"type\":\"status\",\"data\":\"listening\"}");
        await Pause(0.15, g.Frame);
        Eq(g.S.State, AgentState.Speaking, "a cough is not an interruption");
        s.Send("{\"type\":\"status\",\"data\":\"interrupted\"}");
        s.Send("{\"type\":\"transcript_truncated\",\"role\":\"agent\",\"data\":\"A long…\",\"full\":\"A long story about dragons\",\"emitted\":true}");
        await Until(() => cut.Count == 1, g.Frame);
        Eq(g.S.State, AgentState.PlayerTalking, "interrupted");
        Check(!g.S.Playback.IsPlaying && !g.Lips.HasVisemes, "and silent");
        Same(cut, new[] { "A long…" }, "what was heard");
        Eq(g.S.GetTranscript().Last().Text, "A long…", "also in the transcript");
        Reply(s, 2, "Yes?", 0.3);
        await g.State(AgentState.Speaking);
        Check(g.S.State == AgentState.Speaking && g.S.HeardSeconds < 0.3f, "the next reply plays from its start");
        s.Audio16(Tone(0.5), 1);
        await Pause(0.1, g.Frame);
        Check(g.S.Playback.BufferedSeconds < 0.3f, "and what was left of the old one does not");
        g.S.Dispose();
    }

    static async Task AgentFiller()
    {
        // As production does it: the call, the filler as a generation of its own, the reply's text, the reply as another one.
        var g = new Stage();
        g.S.AddAction("look", "Look", a => "Nothing.");
        int visemes = 0;
        g.S.Visemes += l => visemes += l.Count;
        var s = await g.Talk();
        void Filler(int gen, double seconds)
        {
            s.Send("{\"type\":\"status\",\"data\":\"listening\"}");
            s.Send("{\"type\":\"tool_call\",\"name\":\"look\",\"arguments\":\"{}\",\"call_id\":\"c" + gen + "\"}");
            s.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":" + gen + "}");
            s.Send("{\"type\":\"status\",\"data\":\"speaking\",\"generation\":" + gen + "}");
            s.Send("{\"type\":\"visemes\",\"generation\":" + gen + ",\"data\":[{\"v\":1,\"c\":\"PP\",\"t\":0,\"d\":0.1}]}");
            s.Audio16(Tone(seconds), gen);
        }
        Filler(1, 0.6);
        await Pause(0.5, g.Frame);
        Check(g.S.HeardSeconds == 0 && !g.S.Playback.IsPlaying && visemes == 0 && g.S.State == AgentState.Thinking, "what it would say while an action runs is not heard");
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"There is nothing.\"}");
        await Pause(0.2, g.Frame);
        Reply(s, 2, "There is nothing.", 0.5);
        await g.State(AgentState.Speaking);
        await g.State(AgentState.Ready);
        Check(Math.Abs(g.S.HeardSeconds - 0.5) < 0.1, "the reply is, and only it: " + g.S.HeardSeconds);
        // What the model wrote before the action, its plan for instance, is as long as it likes.
        Filler(6, 4.5);
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Nothing.\"}");
        await Pause(3.5, g.Frame);
        Check(!g.S.Playback.IsPlaying && g.S.State == AgentState.Thinking, "nor is a long one");
        Reply(s, 7, "Nothing.", 0.5);
        await g.State(AgentState.Speaking);
        await g.State(AgentState.Ready);
        Check(Math.Abs(g.S.HeardSeconds - 0.5) < 0.1, "only the reply after it: " + g.S.HeardSeconds);
        // A server that says nothing meanwhile: the text comes before the voice.
        s.Send("{\"type\":\"status\",\"data\":\"listening\"}");
        s.Send("{\"type\":\"tool_call\",\"name\":\"look\",\"arguments\":\"{}\",\"call_id\":\"d\"}");
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Nothing.\"}");
        Reply(s, 3, "Nothing.", 0.5);
        await g.State(AgentState.Speaking);
        Eq(g.S.State, AgentState.Speaking, "a reply with no filler before it is heard");
        await g.State(AgentState.Ready);
        // The reply itself, taken for a filler: late, not lost.
        Filler(4, 0.6);
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Nothing.\"}");
        await Until(() => g.S.State == AgentState.Speaking, g.Frame);
        Eq(g.S.State, AgentState.Speaking, "and one that nothing follows is played after all");
        await g.State(AgentState.Ready);
        // The player had only paused, and goes on: what was held is never heard.
        Filler(10, 0.6);
        await Pause(0.1, g.Frame);
        s.Send("{\"type\":\"status\",\"data\":\"listening\"}");
        s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Nothing.\"}");
        bool heardHeld = false;
        for (int i = 0; i < 36; i++)
        {
            await Pause(0.1, g.Frame);
            heardHeld |= g.S.Playback.IsPlaying;
        }
        Check(!heardHeld, "nor when the player goes on talking");
        g.S.ActionFiller = true;
        Filler(11, 0.4);
        await g.State(AgentState.Speaking);
        Eq(g.S.State, AgentState.Speaking, "ActionFiller lets it be heard");
        g.S.ActionFiller = false;
        // Words before the action: heard, unless the action has to come before any.
        foreach (bool spoken in new[] { true, false })
        {
            g.S.SpeakBeforeActions = spoken;
            int gen = spoken ? 20 : 30;
            s.Send("{\"type\":\"status\",\"data\":\"interrupted\"}");
            await Pause(0.3, g.Frame);
            Reply(s, gen, "", 0.6);
            await g.State(AgentState.Speaking);
            s.Send("{\"type\":\"tool_call\",\"name\":\"look\",\"arguments\":\"{}\",\"call_id\":\"w\"}");
            await Pause(0.2, g.Frame);
            s.Audio16(Tone(0.6), gen);
            await Pause(1.0, g.Frame);
            Check(spoken ? g.S.HeardSeconds > 0.9 : g.S.HeardSeconds == 0 && g.S.State == AgentState.Thinking, "words before an action, heard " + spoken + ": " + g.S.HeardSeconds);
            s.Send("{\"type\":\"transcript\",\"role\":\"agent\",\"data\":\"Nothing.\"}");
            Reply(s, gen + 1, "Nothing.", 0.5);
            await g.State(AgentState.Speaking);
            await g.State(AgentState.Ready);
            Check(Math.Abs(g.S.HeardSeconds - 0.5) < 0.1, "and the reply after it: " + g.S.HeardSeconds);
        }
        // The next line of a player who has not been heard to stop: no "listening", only what they said.
        Reply(s, 40, "", 0.6);
        await g.State(AgentState.Speaking);
        s.Send("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"Look.\"}");
        s.Send("{\"type\":\"tool_call\",\"name\":\"look\",\"arguments\":\"{}\",\"call_id\":\"w40\"}");
        await Pause(0.2, g.Frame);
        s.Audio16(Tone(0.6), 40);
        await Pause(1.0, g.Frame);
        Check(g.S.HeardSeconds == 0 && g.S.State == AgentState.Thinking, "nor before the action of the player's next line: " + g.S.HeardSeconds);
        Reply(s, 41, "Nothing.", 0.3);
        await g.State(AgentState.Speaking);
        await g.State(AgentState.Ready);
        // A server that numbers the turn before the action, and answers with that number.
        s.Send("{\"type\":\"status\",\"data\":\"listening\"}");
        await Pause(0.1, g.Frame);
        s.Send("{\"type\":\"status\",\"data\":\"thinking\",\"generation\":50}");
        s.Send("{\"type\":\"tool_call\",\"name\":\"look\",\"arguments\":\"{}\",\"call_id\":\"w50\"}");
        await Pause(0.2, g.Frame);
        Reply(s, 50, "Nothing.", 0.5);
        await g.State(AgentState.Speaking);
        await g.State(AgentState.Ready);
        Check(Math.Abs(g.S.HeardSeconds - 0.5) < 0.1, "a reply of which nothing had been said is heard: " + g.S.HeardSeconds);
        g.S.ActionFiller = true;
        bool others = true;
        g.S.OthersAudible = () => others;
        async Task<short> Heard()
        {
            s.ClearAudio();
            g.S.SendMicrophone(Enumerable.Repeat(0.5f, 1600).ToArray(), 0, 1600, 16000);
            await Until(() => s.AudioBytes == 3200, g.Frame);
            return s.Sample(50);
        }
        await Pause(1.5, g.Frame);
        Eq(await Heard(), (short)0, "it does not listen while another character speaks");
        others = false;
        Check(await Heard() > 10000, "and then it does");
        g.S.Dispose();
    }

    static async Task AgentMute()
    {
        var g = new Stage();
        var s = await g.Talk();
        var loud = Enumerable.Repeat(0.5f, 1600).ToArray();
        async Task<short> Heard()
        {
            s.ClearAudio();
            g.S.SendMicrophone(loud, 0, loud.Length, 16000);
            await Until(() => s.AudioBytes == 3200, g.Frame);
            return s.Sample(50);
        }
        Check(await Heard() > 10000, "the microphone is heard");
        Reply(s, 1, "Hello", 1.0);
        await Until(() => g.S.State == AgentState.Speaking && g.S.HeardSeconds > 0.1f, g.Frame);
        Eq(await Heard(), (short)0, "but not while the character speaks");
        g.S.MuteMicWhileSpeaking = false;
        Check(await Heard() > 10000, "unless players wear headphones");
        g.S.Dispose();
    }

    static async Task AgentBackground()
    {
        // An agent with a background sound never stops sending audio. Its replies must still end.
        var g = new Stage();
        var s = await g.Talk();
        Reply(s, 1, "Hello", 0.4);
        await g.State(AgentState.Speaking);
        for (int i = 0; i < 40 && g.S.State == AgentState.Speaking; i++)
        {
            s.Audio16(Tone(0.1, 0.01), 1);
            await Pause(0.1, g.Frame);
        }
        Eq(g.S.State, AgentState.Ready, "the reply ends although the room goes on");
        for (int i = 0; i < 6; i++)
        {
            s.Audio16(Tone(0.1, 0.01), 1);
            await Pause(0.1, g.Frame);
        }
        Check(g.S.State == AgentState.Ready && !g.S.IsSpeaking, "and the room is not a reply");
        s.Audio16(Tone(0.3, 0.01), 1);
        await Pause(0.05, g.Frame);
        s.ClearAudio();
        g.S.SendMicrophone(Enumerable.Repeat(0.5f, 1600).ToArray(), 0, 1600, 16000);
        await Until(() => s.AudioBytes == 3200, g.Frame);
        Check(g.S.Playback.IsPlaying && s.Sample(50) > 10000, "nor does it mute the microphone");
        g.S.Dispose();
    }

    static async Task AgentContext()
    {
        var g = new Stage(new SessionOptions { Persona = "You are a king.", ContextHeading = "Ahora:" });
        g.S.SetContext("Mood", "bored");
        var s = await g.Talk();
        string Prompt() => (string)s.Data("prompt");
        Check(Prompt().StartsWith("You are a king.") && Prompt().Contains("- Mood: bored"), "context goes with the persona");
        Check(Prompt().Contains("\n\nAhora:\n- Mood"), "under a heading of the game's");
        int before = s.Types().Count(t => t == "prompt");
        g.S.SetContext("Mood", "amused");
        g.S.SetContext("Time left", "20 s");
        g.S.SetContext("Mood", "amused");
        await Pause(0.5, g.Frame);
        Eq(s.Types().Count(t => t == "prompt"), before + 1, "changes in one moment are one message");
        Check(Prompt().Contains("- Mood: amused") && Prompt().Contains("- Time left: 20 s"), "with both");
        g.S.SetContext("Time left", null);
        await Pause(0.4, g.Frame);
        Check(!Prompt().Contains("Time left"), "null takes one out");
        g.S.Dispose();

        g = new Stage(new SessionOptions { AgentId = "a1" });
        g.S.SetContext("Mood", "bored");
        s = await g.Talk();
        await Pause(0.4, g.Frame);
        Check(s.Last("prompt") == null, "a dashboard agent keeps its own prompt");
        g.S.Dispose();
    }

    static async Task AgentUpdates()
    {
        var g = new Stage();
        var s = await g.Talk();
        g.S.SetVoice("M4");
        g.S.SetLanguage("auto");
        g.S.SetPitch(4f);
        await Until(() => s.Last("pitch") != null, g.Frame);
        Check((string)s.Data("voice") == "M4" && (string)s.Data("language") == "" && (double)s.Data("pitch") == 4.0, "sent");
        s.Send("{\"type\":\"transcript\",\"role\":\"user\",\"data\":\"Hi\"}");
        await Until(() => g.S.GetTranscript().Count == 1, g.Frame);
        g.S.ResetConversation();
        await Until(() => s.Last("reset") != null, g.Frame);
        Check(s.Last("reset") != null && g.S.GetTranscript().Count == 0, "ResetConversation");
        g.S.Dispose();
    }

    static async Task AgentReconnect()
    {
        var g = new Stage(new SessionOptions { Persona = "You are Brom." });
        var log = new List<string>();
        g.S.StateChanged += st => log.Add(st.ToString());
        g.S.Reconnecting += () => log.Add("reconnecting");
        g.S.Reconnected += () => log.Add("reconnected");
        g.S.Disconnected += r => log.Add("ended");
        var s = await g.Talk();
        Reply(s, 5, "A long story", 5);
        await g.State(AgentState.Speaking);
        g.S.SetPersona("You are tired.");
        s.Drop();
        await Until(() => log.Contains("reconnecting"), g.Frame);
        Check(g.S.State == AgentState.Connecting && !g.S.Playback.IsPlaying, "connecting, and silent");
        await Until(() => log.Contains("reconnected"), g.Frame, 5);
        await Configured(g.Frame);
        Eq((string)server.Current.Data("prompt"), "You are tired.", "as it was left");
        server.Current.Send("{\"type\":\"status\",\"data\":\"connected\",\"session_id\":\"y\"}");
        await g.State(AgentState.Ready);
        Reply(server.Current, 1, "Where was I?", 0.3);
        await g.State(AgentState.Speaking);
        Eq(g.S.State, AgentState.Speaking, "and it talks again");
        Check(!log.Contains("ended"), "the conversation did not end");
        g.S.Dispose();
    }

    static async Task AgentEnd()
    {
        var g = new Stage();
        var ended = new List<string>();
        var errors = new List<string>();
        g.S.Disconnected += r => ended.Add(r);
        g.S.Error += e => errors.Add(e.Code);
        var s = await g.Talk();
        s.Close(1008, "account balance exhausted");
        await Until(() => ended.Count == 1, g.Frame);
        Same(ended, new[] { "account balance exhausted" }, "the server's reason");
        Eq(g.S.State, AgentState.Disconnected, "disconnected");
        s = await g.Talk();
        Eq(g.S.State, AgentState.Ready, "can start again");
        _ = g.S.DisconnectAsync();
        g.S.Update();
        Eq(ended.Count, 2, "stopped");
        Eq(g.S.State, AgentState.Disconnected, "at once");
        await g.S.DisconnectAsync();
        g.S.Update();
        Eq(ended.Count, 2, "once");
        server.Refuse = true;
        Exception thrown = null;
        try { await g.S.ConnectAsync(); }
        catch (IOException e) { thrown = e; }
        g.S.Update();
        Check(thrown != null && thrown.InnerException is LokutorException l && l.Error.Code == "ws.close", "a refused connection throws its reason");
        Check(g.S.State == AgentState.Disconnected && ended.Count == 2 && errors.Count == 0, "and nothing else");
    }

    // ---- Written lines ---------------------------------------------------------------------------------

    /// <summary>A character that says lines, with a sound card that plays in real time.</summary>
    sealed class Speaker
    {
        public readonly VoiceLines Lines;
        public readonly Dictionary<string, VoiceLine> Kept = new Dictionary<string, VoiceLine>();
        public readonly List<string> Log = new List<string>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;

        public Speaker(bool keep)
        {
            Lines = new VoiceLines(NewTts(), new PlaybackBuffer(LokutorApi.OutputRate), new LipSync());
            if (keep)
            {
                Lines.Load = key => { lock (Kept) return Kept.TryGetValue(key, out var l) ? VoiceLine.FromBytes(l.ToBytes()) : null; };
                Lines.Store = (key, line) => { lock (Kept) Kept[key] = line; };
            }
            Lines.LineStarted += t => Log.Add("start " + t);
            Lines.LineFinished += t => Log.Add("end " + t);
            Lines.Error += e => Log.Add(e.Code);
        }

        public void Frame()
        {
            double now = clock.Elapsed.TotalSeconds;
            var block = new float[(int)(now * LokutorApi.OutputRate) - (int)(last * LokutorApi.OutputRate)];
            float delta = (float)(now - last);
            last = now;
            Lines.Update();
            Lines.Playback.Read(block, 0, block.Length);
            Lines.LipSync.Advance(Lines.Playback.HeardSeconds, Lines.Playback.HeardLevel, Lines.IsSpeaking && Lines.Playback.IsAudible, delta);
        }

        public async Task<bool> Say(string text, SayOptions o = null)
        {
            var said = Lines.SayAsync(text, o);
            await Until(() => said.IsCompleted, Frame);
            return said.IsCompleted && said.Result;
        }
    }

    static async Task LinesKept()
    {
        var line = new VoiceLine { SampleRate = 24000, Audio = new byte[] { 1, 2, 3, 4 }, Visemes = new List<Viseme> { new Viseme(10, 0.5f, 0.25f) } };
        var back = VoiceLine.FromBytes(line.ToBytes());
        Check(back != null && back.SampleRate == 24000 && back.Audio.SequenceEqual(line.Audio) && back.Visemes.Count == 1 && back.Visemes[0].C == "aa" && back.Visemes[0].T == 0.5f,
              "a line survives its file");
        Check(VoiceLine.FromBytes(line.ToBytes().Take(20).ToArray()) == null && VoiceLine.FromBytes(new byte[40]) == null, "a damaged file is no line");

        var v = new Speaker(true);
        var clock = Stopwatch.StartNew();
        Check(await v.Say("Halt!"), "said");
        Check(clock.Elapsed.TotalSeconds > 0.38, "done once it has been heard");
        Same(v.Log, new[] { "start Halt!", "end Halt!" }, "events");
        Check(v.Lines.HasLine("Halt!") && v.Kept.Values.First().Visemes.Count == 1, "kept, with its mouth shapes");
        Check(await v.Say("Halt!"), "said again");
        Check(server.Count == 1 && server.Current.Text.Count == 1, "without asking again");
        Check(!v.Lines.HasLine("Halt!", new SayOptions { Voice = "M1" }), "another voice is another line");
        var preload = v.Lines.PreloadAsync(new[] { "Who goes there?", "Halt!" });
        await Until(() => preload.IsCompleted, v.Frame);
        Check(v.Lines.HasLine("Who goes there?") && server.Current.Text.Count == 2, "preloaded, only what was missing");
        var shipped = new List<string>();
        var save = v.Lines.SaveAsync((key, l) => shipped.Add(key), new[] { "Halt!", "Move along." });
        await Until(() => save.IsCompleted, v.Frame);
        Check(shipped.Count == 2 && shipped[0] == VoiceLine.KeyFor("F1", "en", "Halt!") && server.Current.Text.Count == 3, "written elsewhere, for the game to ship");
        v.Log.Clear();
        Check(!await v.Say("bad") && !v.Lines.IsSpeaking, "a line that cannot be said");
        Same(v.Log, new[] { "start bad", "validation.invalid_voice", "end bad" }, "says why");
    }

    static async Task LinesQueue()
    {
        var v = new Speaker(false);
        var a = v.Lines.SayAsync("A");
        var b = v.Lines.SayAsync("B");
        await Until(() => a.IsCompleted && b.IsCompleted, v.Frame);
        Same(v.Log, new[] { "start A", "end A", "start B", "end B" }, "lines wait for each other");
        Check(a.Result && b.Result, "both heard");
        var c = v.Lines.SayAsync("C");
        var waiting = v.Lines.SayAsync("never");
        await Until(() => v.Lines.Playback.HeardSeconds > 0.1f, v.Frame);
        Check(v.Lines.LipSync.MouthOpen > 0f, "the mouth moves");
        var d = v.Lines.SayAsync("D", new SayOptions { Interrupt = true });
        Check(c.IsCompleted && !c.Result && waiting.IsCompleted && !waiting.Result, "an urgent line cuts the one being said, and the ones waiting");
        await Until(() => d.IsCompleted, v.Frame);
        Check(d.Result, "and is heard");
        Check(!v.Log.Contains("start never"), "in their place");
        c = v.Lines.SayAsync("silent");          // the server has not answered yet
        await Until(() => v.Lines.IsSpeaking && server.Current.Text.Any(m => (string)m["text"] == "silent"), v.Frame);
        v.Lines.Stop();
        Check(c.IsCompleted && !c.Result && !v.Lines.IsSpeaking, "Stop");
        Check(await v.Say("E"), "and the next line is said");
        await Until(() => false, v.Frame, 0.3);          // a line that was stopped still hears from its request
        Check(new[] { "C", "silent", "E" }.All(x => v.Log.Count(l => l == "end " + x) == 1), "each line ends once");
    }
}
