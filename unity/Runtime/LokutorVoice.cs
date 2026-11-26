// Says written lines (barks, narration, tutorials) in a character's voice, from its place in the world, moving its mouth.
// Lines are kept on disk, so each is made once; they can also ship with the game and need no network at all. For a
// character that listens and answers use LokutorAgent.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Lokutor
{
    [AddComponentMenu("Lokutor/Lokutor Voice")]
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class LokutorVoice : MonoBehaviour
    {
        [Tooltip("Your Lokutor API key. Empty: LokutorAgent.DefaultApiKey, then the LOKUTOR_API_KEY environment variable. A shipped game should ship its lines, or use TicketProvider.")]
        public string apiKey = "";

        [Tooltip("F1-F5 (female) or M1-M5 (male), or a voice id from the Lokutor dashboard.")]
        public string voice = "F1";
        public string language = "en";

        [Tooltip("The voice comes from the character's position (3D). Off: heard the same everywhere (2D).")]
        public bool spatialVoice = true;

        [Tooltip("Agents do not listen while this voice is heard (see Lokutor Agent's Mute Mic While Speaking). Off for a voice too far away to reach the player's microphone.")]
        public bool mutesMicrophones = true;

        [Tooltip("Keep lines on disk (persistentDataPath/lokutor_voices), so each is made only once.")]
        public bool keepLines = true;

        [Tooltip("The folder, inside a Resources folder, of the lines that ship with the game (see SaveLinesTo).")]
        public string shippedLines = "LokutorVoices";

        [Tooltip("Empty: the Lokutor Lip Sync on this object, under it or above it, or a new one that finds the mouth by itself.")]
        public LokutorLipSync lipSync;

        [Header("Events")]
        public LokutorTextEvent onLineStarted = new LokutorTextEvent();
        public LokutorTextEvent onLineFinished = new LokutorTextEvent();

        /// <summary>For a shipped game: returns a short-lived ticket from YOUR server.</summary>
        public Func<Task<string>> TicketProvider;
        [NonSerialized] public string url = LokutorApi.TTSUrl;

        public event Action<LokutorError> Error;

        /// <summary>The queue of lines, without the engine.</summary>
        public VoiceLines Lines { get { Prepare(); return lines; } }
        public LokutorVoicePlayer Player { get { Prepare(); return player; } }
        public bool IsSpeaking => lines != null && lines.IsSpeaking;
        public float MouthOpen => lipSync != null ? lipSync.MouthOpen : 0f;
        /// <summary>How much of the line has been heard, in seconds.</summary>
        public float HeardSeconds => lines != null ? lines.Playback.HeardSeconds : 0f;

        VoiceLines lines;
        LokutorVoicePlayer player;
        float audibleUntil = -1f;
        static readonly List<LokutorVoice> voices = new List<LokutorVoice>();

        // The room's echo reaches the microphone for a moment after the voice.
        internal static bool AnyAudible
        {
            get
            {
                foreach (var v in voices)
                    if (v.mutesMicrophones && Time.unscaledTime < v.audibleUntil) return true;
                return false;
            }
        }

        void OnEnable() => voices.Add(this);
        string cacheFolder;

        void Prepare()
        {
            if (lines != null) return;
            cacheFolder = Path.Combine(Application.persistentDataPath, "lokutor_voices");
            lipSync = LokutorAgent.FindLipSync(this, lipSync);
            var source = GetComponent<AudioSource>();
            source.spatialBlend = spatialVoice ? 1f : 0f;
            lines = new VoiceLines(new TTSClient { UserAgent = LokutorAgent.UserAgent }, new PlaybackBuffer(LokutorApi.OutputRate), lipSync.Core);
            lines.Load = Load;
            lines.Store = (key, line) => { if (keepLines) Write(cacheFolder, key, line); };
            lines.LineStarted += t => onLineStarted.Invoke(t);
            lines.LineFinished += t => onLineFinished.Invoke(t);
            lines.Error += e =>
            {
                Debug.LogWarning("[Lokutor] " + e, this);
                Error?.Invoke(e);
            };
            player = new LokutorVoicePlayer(source, lines.Playback);
        }

        void Configure()
        {
            Prepare();
            lines.Voice = voice;
            lines.Language = language;
            lines.Tts.ApiKey = !string.IsNullOrEmpty(apiKey) ? apiKey : LokutorAgent.DefaultApiKey;
            lines.Tts.Auth = TicketProvider;
            lines.Tts.ServerUrl = url;
        }

        /// <summary>
        /// Says a line; awaited, returns when it has been heard (true) or could not be said (false). Lines wait for each
        /// other, unless options.Interrupt.
        /// </summary>
        public Task<bool> Say(string text, SayOptions options = null)
        {
            Configure();
            return lines.SayAsync(text, options);
        }

        /// <summary>The same for a UnityEvent or a coroutine's fire and forget.</summary>
        public void SayLine(string text) => Say(text);

        /// <summary>For coroutines: yield return voice.SayAndWait("...").</summary>
        public IEnumerator SayAndWait(string text, SayOptions options = null)
        {
            var heard = Say(text, options);
            while (!heard.IsCompleted) yield return null;
        }

        /// <summary>Stops at once and forgets the lines waiting.</summary>
        public void Stop() => lines?.Stop();

        /// <summary>Makes lines before they are needed, so they start at once.</summary>
        public Task PreloadLines(IEnumerable<string> texts, SayOptions options = null)
        {
            Configure();
            return lines.PreloadAsync(texts, options);
        }

        /// <summary>Whether a line is on disk or ships with the game.</summary>
        public bool HasLine(string text, SayOptions options = null)
        {
            Configure();
            return lines.HasLine(text, options);
        }

        /// <summary>
        /// Writes lines into a folder of the project, so they ship with the game and need no network or key: in the editor,
        /// SaveLinesTo("Assets/Resources/LokutorVoices", lines).
        /// </summary>
        public Task SaveLinesTo(string folder, IEnumerable<string> texts, SayOptions options = null)
        {
            Configure();
            return lines.SaveAsync((key, line) => Write(folder, key, line), texts, options);
        }

        VoiceLine Load(string key)
        {
            string file = Path.Combine(cacheFolder, key + ".bytes");
            if (keepLines && File.Exists(file)) return VoiceLine.FromBytes(File.ReadAllBytes(file));
            var shipped = string.IsNullOrEmpty(shippedLines) ? null : Resources.Load<TextAsset>(shippedLines + "/" + key);
            return shipped != null ? VoiceLine.FromBytes(shipped.bytes) : null;
        }

        static void Write(string folder, string key, VoiceLine line)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, key + ".bytes"), line.ToBytes());
            }
            catch (IOException e) { Debug.LogWarning("[Lokutor] The line could not be kept: " + e.Message); }
        }

        void Update()
        {
            if (lines == null) return;
            lines.Update();
            player.Update();
            if (lines.IsSpeaking && lines.Playback.IsAudible) audibleUntil = Time.unscaledTime + 0.3f;
            if (lipSync != null) lipSync.Feed(this, lines.Playback.HeardSeconds, lines.Playback.HeardLevel, lines.IsSpeaking && lines.Playback.IsAudible, Time.deltaTime);
        }

        void OnDisable()
        {
            voices.Remove(this);
            if (lines == null) return;
            lines.Stop();
            lines.Tts.Close();
            player.Stop();
            if (lipSync != null) lipSync.Release(this);
        }
    }
}
