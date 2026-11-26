// Written lines said by a character (barks, narration, tutorials), without an engine. LokutorVoice (Unity) plays them
// from the character's place in the world and moves its mouth.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    /// <summary>A line as it is kept: the voice and its mouth shapes.</summary>
    public sealed class VoiceLine
    {
        public int SampleRate = LokutorApi.OutputRate;
        /// <summary>16-bit PCM, mono.</summary>
        public byte[] Audio = new byte[0];
        public List<Viseme> Visemes = new List<Viseme>();

        static readonly byte[] Magic = Encoding.ASCII.GetBytes("LKV1");

        /// <summary>The name a line is kept under: the same voice, language and words give the same one.</summary>
        public static string KeyFor(string voice, string language, string text)
        {
            using (var sha = SHA1.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(voice + "|" + language + "|" + text));
                var sb = new StringBuilder();
                for (int i = 0; i < 10; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public byte[] ToBytes()
        {
            using (var m = new MemoryStream())
            using (var w = new BinaryWriter(m))
            {
                w.Write(Magic);
                w.Write(SampleRate);
                w.Write(Visemes.Count);
                foreach (var v in Visemes)
                {
                    w.Write((byte)v.V);
                    w.Write(v.T);
                    w.Write(v.D);
                }
                w.Write(Audio.Length);
                w.Write(Audio);
                w.Flush();
                return m.ToArray();
            }
        }

        /// <summary>Null when the bytes are not a line.</summary>
        public static VoiceLine FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return null;
            for (int i = 0; i < Magic.Length; i++)
                if (bytes[i] != Magic[i]) return null;
            try
            {
                using (var r = new BinaryReader(new MemoryStream(bytes)))
                {
                    r.ReadBytes(Magic.Length);
                    var line = new VoiceLine { SampleRate = r.ReadInt32() };
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) line.Visemes.Add(new Viseme(r.ReadByte(), r.ReadSingle(), r.ReadSingle()));
                    int length = r.ReadInt32();
                    line.Audio = r.ReadBytes(length);
                    return line.SampleRate > 0 && line.Audio.Length == length ? line : null;
                }
            }
            catch (EndOfStreamException) { return null; }
        }
    }

    public sealed class SayOptions
    {
        /// <summary>Null: the character's.</summary>
        public string Voice, Language;
        /// <summary>Say it now, dropping what is being said.</summary>
        public bool Interrupt;
    }

    /// <summary>Says lines one after another through a <see cref="PlaybackBuffer"/>, and keeps them.</summary>
    public sealed class VoiceLines
    {
        public string Voice = "F1", Language = "en";

        /// <summary>Where lines come from before the network: from disk, from the game's own files. By key; null when it is not there.</summary>
        public Func<string, VoiceLine> Load;
        /// <summary>Where they are kept.</summary>
        public Action<string, VoiceLine> Store;

        public event Action<string> LineStarted;
        public event Action<string> LineFinished;
        public event Action<LokutorError> Error;

        public TTSClient Tts { get; }
        public PlaybackBuffer Playback { get; }
        public LipSync LipSync { get; }
        /// <summary>The line being said, or "".</summary>
        public string Current { get; private set; } = "";
        public bool IsSpeaking => Current.Length > 0;

        sealed class Turn
        {
            public string Text;
            public SayOptions Options;
            public TaskCompletionSource<bool> Heard = new TaskCompletionSource<bool>();
            public CancellationTokenSource Cancel = new CancellationTokenSource();
            public bool Started, Arrived;
        }

        readonly List<Turn> queue = new List<Turn>();
        readonly ConcurrentQueue<Action> arrived = new ConcurrentQueue<Action>();

        public VoiceLines(TTSClient tts, PlaybackBuffer playback, LipSync lipSync)
        {
            Tts = tts;
            Playback = playback;
            LipSync = lipSync;
        }

        /// <summary>Says a line. The task ends when it has been heard (true) or could not be said (false). Lines wait for each other.</summary>
        public Task<bool> SayAsync(string text, SayOptions options = null)
        {
            options = options ?? new SayOptions();
            if (options.Interrupt) Stop();
            var turn = new Turn { Text = text, Options = options };
            queue.Add(turn);
            return turn.Heard.Task;
        }

        /// <summary>Stops at once and forgets the lines waiting.</summary>
        public void Stop()
        {
            var dropped = queue.ToArray();
            queue.Clear();
            Playback.Clear();
            LipSync.Clear();
            Current = "";
            foreach (var t in dropped)
            {
                t.Cancel.Cancel();
                if (t.Started) LineFinished?.Invoke(t.Text);
                t.Heard.TrySetResult(false);
            }
        }

        /// <summary>Call once per frame, on the thread SayAsync is called from.</summary>
        public void Update()
        {
            while (arrived.TryDequeue(out var a)) a();
            if (queue.Count == 0) return;
            var t = queue[0];
            if (!t.Started)
            {
                t.Started = true;
                Current = t.Text;
                Playback.Clear();
                LipSync.Clear();
                LineStarted?.Invoke(t.Text);
                Speak(t);
            }
            else if (t.Arrived && !Playback.IsAudible) Finish(t, true);
        }

        void Finish(Turn t, bool heard)
        {
            if (!queue.Remove(t)) return;          // Stop() got there first
            Current = "";
            LineFinished?.Invoke(t.Text);
            t.Heard.TrySetResult(heard);
        }

        async void Speak(Turn t)
        {
            string key = Key(t.Text, t.Options);
            var kept = LoadLine(key);
            if (kept != null)
            {
                Playback.SampleRate = kept.SampleRate;
                Playback.Write(Pcm16.ToFloat(kept.Audio));
                LipSync.PushVisemes(kept.Visemes);
                t.Arrived = true;
                return;
            }
            Playback.SampleRate = LokutorApi.OutputRate;
            // Whatever thread it comes on, Update() deals with it, in order.
            var got = await Fetch(t.Text, t.Options, t.Cancel.Token,
                pcm => arrived.Enqueue(() => { if (queue.Contains(t)) Playback.Write(Pcm16.ToFloat(pcm)); }),
                list => arrived.Enqueue(() => { if (queue.Contains(t)) LipSync.PushVisemes(list); }));
            arrived.Enqueue(() =>
            {
                if (got.Ok)
                {
                    StoreLine(key, got);
                    t.Arrived = true;
                    return;
                }
                if (got.Error.Code != "internal.cancelled") Error?.Invoke(got.Error);
                Finish(t, false);
            });
        }

        Task<TTSResult> Fetch(string text, SayOptions o, CancellationToken cancel, Action<byte[]> onAudio = null, Action<IReadOnlyList<Viseme>> onVisemes = null) =>
            Tts.SynthesizeAsync(text, new SynthesizeOptions
            {
                Voice = o.Voice ?? Voice, Language = o.Language ?? Language, Visemes = true, OnAudio = onAudio, OnVisemes = onVisemes,
            }, cancel);

        /// <summary>Fetches lines before they are needed, so they start at once and, once kept, work without a network.</summary>
        public Task PreloadAsync(IEnumerable<string> lines, SayOptions options = null) => SaveAsync(Store, lines, options);

        /// <summary>The same, into a place of your choice (the game's own files): store(key, line).</summary>
        public async Task SaveAsync(Action<string, VoiceLine> store, IEnumerable<string> lines, SayOptions options = null)
        {
            options = options ?? new SayOptions();
            foreach (var text in lines)
            {
                string key = Key(text, options);
                var line = LoadLine(key);
                if (line == null)
                {
                    var got = await Fetch(text, options, default);
                    if (got.Ok) line = Keep(got);
                    else Error?.Invoke(got.Error);
                }
                else if (store == Store) continue;        // kept already
                if (line != null) store?.Invoke(key, line);
            }
        }

        /// <summary>Whether a line is already kept.</summary>
        public bool HasLine(string text, SayOptions options = null) => LoadLine(Key(text, options ?? new SayOptions())) != null;

        string Key(string text, SayOptions o) => VoiceLine.KeyFor(o.Voice ?? Voice, o.Language ?? Language, text);

        VoiceLine LoadLine(string key) => Load != null ? Load(key) : null;

        void StoreLine(string key, TTSResult got)
        {
            if (Store != null && got.Audio.Length > 0) Store(key, Keep(got));
        }

        static VoiceLine Keep(TTSResult got) => new VoiceLine { SampleRate = got.SampleRate, Audio = got.Audio, Visemes = got.Visemes };
    }
}
