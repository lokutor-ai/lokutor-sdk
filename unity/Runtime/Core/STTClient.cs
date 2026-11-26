using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    public sealed class TranscribeOptions
    {
        /// <summary>Rate of raw PCM (16000 if not set). A WAV carries its own.</summary>
        public int SampleRate;
        /// <summary>"pcm16" or "wav". Not set: found from the bytes.</summary>
        public string Format;
        public string Language;
        /// <summary>Names and terms to write your way when what is heard is close.</summary>
        public List<string> Keyterms;
    }

    public sealed class TranscribeSegment
    {
        public string Text;
        public float Start, End, Duration;
    }

    public sealed class TranscribeResult
    {
        public bool Ok => Error == null;
        public string Text = "", Engine = "", Language = "";
        public float LatencyMs, DurationSeconds;
        public int SampleRate;
        public List<TranscribeSegment> Segments = new List<TranscribeSegment>();
        public LokutorError Error;
    }

    /// <summary>
    /// Transcribes one complete recording (POST /stt/transcribe), as STTClient in the JavaScript and Python SDKs. For
    /// speech as it happens use <see cref="SpeechToTextClient"/>.
    /// </summary>
    public sealed class STTClient
    {
        public string ApiKey;
        public Func<Task<string>> Auth;
        public string ServerUrl = LokutorApi.STTUrl;

        public STTClient(string apiKey = null) { ApiKey = apiKey; }

        /// <summary>Audio is 16-bit PCM mono (set SampleRate if it is not 16 kHz) or a whole WAV file's bytes.</summary>
        public async Task<TranscribeResult> TranscribeAsync(byte[] audio, TranscribeOptions options = null, CancellationToken cancel = default)
        {
            var o = options ?? new TranscribeOptions();
            string key;
            try { key = Auth != null ? await Auth() : !string.IsNullOrEmpty(ApiKey) ? ApiKey : Environment.GetEnvironmentVariable("LOKUTOR_API_KEY"); }
            catch (Exception e) { return new TranscribeResult { Error = new LokutorError("auth.missing_key", "The auth callback failed.", e.Message, true) }; }
            if (string.IsNullOrEmpty(key)) return new TranscribeResult { Error = new LokutorError("auth.missing_key", "No API key: set ApiKey, or Auth for a shipped game.") };
            bool wav = audio.Length > 12 && audio[0] == 'R' && audio[1] == 'I' && audio[2] == 'F' && audio[3] == 'F';
            var body = new Dictionary<string, object>
            {
                ["audio"] = Convert.ToBase64String(audio),
                ["format"] = !string.IsNullOrEmpty(o.Format) ? o.Format : wav ? "wav" : "pcm16",
            };
            if (o.SampleRate > 0 || !wav) body["sample_rate"] = o.SampleRate > 0 ? o.SampleRate : LokutorApi.InputRate;
            if (!string.IsNullOrEmpty(o.Language)) body["language"] = o.Language;
            if (o.Keyterms != null && o.Keyterms.Count > 0) body["keyterms"] = o.Keyterms;
            var reply = await LokutorApi.RequestJsonAsync(HttpMethod.Post, LokutorApi.HttpOrigin(ServerUrl) + "/stt/transcribe", key,
                MiniJson.Serialize(body), TimeSpan.FromSeconds(60), cancel).ConfigureAwait(false);
            var d = reply.Body;
            var result = new TranscribeResult
            {
                Text = Json.Str(d, "text"), Engine = Json.Str(d, "engine"), Language = Json.Str(d, "language"),
                LatencyMs = (float)Json.Num(d, "latency_ms"), DurationSeconds = (float)Json.Num(d, "duration_seconds"),
                SampleRate = Json.Int(d, "sample_rate"), Error = reply.Error,
            };
            foreach (var s in Json.Objects(d, "segments"))
                result.Segments.Add(new TranscribeSegment
                {
                    Text = Json.Str(s, "text"), Start = (float)Json.Num(s, "start"), End = (float)Json.Num(s, "end"), Duration = (float)Json.Num(s, "duration"),
                });
            return result;
        }
    }

    /// <summary>
    /// Speech to text as the player talks (wss://api.lokutor.com/ws/stt): voice commands, dictation, captions. No language
    /// model and no voice. As SpeechToTextClient in the JavaScript SDK.
    /// </summary>
    public sealed class SpeechToTextClient : EventPump, IDisposable
    {
        public string ApiKey;
        public Func<Task<string>> Auth;
        public string ServerUrl = LokutorApi.STTUrl;
        public string Language = "en";
        /// <summary>"silero" (default) or "rms".</summary>
        public string Vad = "silero";
        /// <summary>Names and terms to write your way when what is heard is close.</summary>
        public List<string> Keyterms;
        public TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        public string UserAgent = "lokutor-csharp/" + LokutorApi.Version;

        // Raised by Update() unless a Dispatcher is set.
        /// <summary>The best guess so far, while the player talks.</summary>
        public event Action<string> PartialTranscript;
        /// <summary>The player finished a sentence.</summary>
        public event Action<string> FinalTranscript;
        public event Action<LokutorError> Error;
        /// <summary>connecting, connected, disconnected.</summary>
        public event Action<string> StatusChange;

        public bool Connected { get { var s = socket; return s != null && s.IsOpen; } }

        volatile LokutorSocket socket;
        IAudioManager managed;
        StreamResampler resampler;
        CancellationTokenSource attempt;

        public SpeechToTextClient(string apiKey = null) { ApiKey = apiKey; }

        /// <summary>Connects, and with an audio manager (LokutorAudioManager in Unity) starts listening. Without one, send the audio yourself.</summary>
        public async Task<bool> ConnectAsync(IAudioManager audio = null)
        {
            if (socket != null) return Connected;
            attempt?.Cancel();
            var mine = attempt = new CancellationTokenSource();
            RaiseStatus("connecting");
            var opened = await LokutorSocket.OpenAsync(ServerUrl, ApiKey, Auth, null, UserAgent, ConnectTimeout, mine.Token);
            if (opened.Error != null)
            {
                if (opened.Error.Code != "internal.cancelled") RaiseError(opened.Error);
                if (attempt == mine) RaiseStatus("disconnected");
                return false;
            }
            var s = opened.Socket;
            if (mine.IsCancellationRequested)
            {
                await s.CloseAsync();
                if (attempt == mine) RaiseStatus("disconnected");
                return false;
            }
            s.OnText = HandleMessage;
            s.OnClosed = (code, reason) =>
            {
                if (socket != s) return;
                socket = null;
                StopManaged();
                if (code != 1000 && code != 1005) RaiseError(new LokutorError("ws.close", "The connection was lost.", reason, true));
                RaiseStatus("disconnected");
            };
            var first = new Dictionary<string, object> { ["language"] = Language, ["vad"] = Vad };
            if (Keyterms != null && Keyterms.Count > 0) first["keyterms"] = Keyterms;
            s.SendText(MiniJson.Serialize(first));
            socket = s;
            s.Start();
            RaiseStatus("connected");
            if (audio == null) return true;
            managed = audio;
            if (audio.StartMicrophone((samples, count, rate) => SendAudioFloat(samples, 0, count, rate))) return true;
            RaiseError(new LokutorError("audio.microphone_unavailable", "The microphone could not be started."));
            Disconnect();
            return false;
        }

        public void Disconnect()
        {
            attempt?.Cancel();
            var s = socket;
            socket = null;
            StopManaged();
            if (s == null) return;
            _ = s.CloseAsync();
            RaiseStatus("disconnected");
        }

        public void Dispose() => Disconnect();

        /// <summary>16-bit PCM, 16 kHz, mono. Keep sending silence: the end of an utterance is measured in it.</summary>
        public void SendAudio(byte[] pcm16)
        {
            var s = socket;
            if (s != null && pcm16 != null && pcm16.Length > 0) s.SendBinary(pcm16);
        }

        /// <summary>The same from float samples at any rate.</summary>
        public void SendAudioFloat(float[] samples, int offset, int count, int rate)
        {
            if (socket == null || count <= 0) return;
            if (resampler == null || resampler.InputRate != rate) resampler = new StreamResampler(rate, LokutorApi.InputRate);
            var r = resampler.Process(samples, offset, count);
            if (r.Length > 0) SendAudio(Pcm16.FromFloat(r, 0, r.Length));
        }

        /// <summary>Finishes whatever is being said now.</summary>
        public void EndUtterance()
        {
            var s = socket;
            if (s != null) s.SendText("{\"type\":\"end\"}");
        }

        internal void HandleMessage(string json)
        {
            Dictionary<string, object> msg;
            try { msg = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (FormatException) { return; }
            if (msg == null) return;
            msg.TryGetValue("data", out var data);
            switch (Json.Str(msg, "type"))
            {
                case "transcript":
                    string text = data as string ?? "";
                    if (Json.Bool(msg, "isFinal")) Post(() => FinalTranscript?.Invoke(text));
                    else Post(() => PartialTranscript?.Invoke(text));
                    break;
                case "error":
                    RaiseError(LokutorError.FromServer(data));
                    break;
            }
        }

        void StopManaged()
        {
            var a = managed;
            managed = null;
            if (a != null) Post(a.StopMicrophone);
        }

        void RaiseStatus(string value) => Post(() => StatusChange?.Invoke(value));

        void RaiseError(LokutorError e) => Post(() => Error?.Invoke(e));

        protected override void OnCallbackFailed(Exception e) => Error?.Invoke(new LokutorError("callback", e.Message, e.GetType().Name, false));
    }
}
