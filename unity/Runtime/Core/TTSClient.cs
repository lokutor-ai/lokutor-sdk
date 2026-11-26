// Text to speech over wss://api.lokutor.com/ws/tts, as TTSClient in the JavaScript and Python SDKs. For a character that
// says written lines use LokutorVoice, which plays them and moves the mouth.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    public sealed class SynthesizeOptions
    {
        public string Voice = "F1";
        public string Language = "en";
        public bool Visemes;
        // Sent only when set: the server's defaults are what production is tuned for.
        public float? Speed;
        public int? Steps;
        public float? Temperature;
        public List<string> DictionaryIds;
        /// <summary>Word -> how to say it.</summary>
        public Dictionary<string, string> Pronunciations;

        /// <summary>16-bit PCM, 44.1 kHz, mono, as it is generated.</summary>
        public Action<byte[]> OnAudio;
        /// <summary>Times are from the request's first sample.</summary>
        public Action<IReadOnlyList<Viseme>> OnVisemes;
        /// <summary>Milliseconds until the first audio.</summary>
        public Action<int> OnTTFB;
        public Action<LokutorError> OnError;
    }

    public sealed class TTSResult
    {
        public bool Ok => Error == null;
        /// <summary>16-bit PCM, mono, at SampleRate.</summary>
        public byte[] Audio = new byte[0];
        public int SampleRate = LokutorApi.OutputRate;
        public List<Viseme> Visemes = new List<Viseme>();
        public LokutorError Error;
        public float Seconds => Audio.Length / 2f / SampleRate;
    }

    public sealed class TTSClient : IDisposable
    {
        public string ApiKey;
        /// <summary>Called for the credential: a ticket from your server, in a shipped game.</summary>
        public Func<Task<string>> Auth;
        public string ServerUrl = LokutorApi.TTSUrl;
        public string UserAgent = "lokutor-csharp/" + LokutorApi.Version;
        /// <summary>Keep the connection open this long after a request, so the next one skips the handshake. It counts as a session.</summary>
        public TimeSpan KeepOpen = TimeSpan.FromSeconds(8);
        public TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        /// <summary>How long to wait for the first audio, and between chunks afterwards.</summary>
        public TimeSpan FirstAudioTimeout = TimeSpan.FromSeconds(30), StallTimeout = TimeSpan.FromSeconds(15);

        sealed class Request
        {
            public TaskCompletionSource<LokutorError> Done = new TaskCompletionSource<LokutorError>(TaskCreationOptions.RunContinuationsAsynchronously);
            public MemoryStream Pcm = new MemoryStream();
            public List<Viseme> Visemes = new List<Viseme>();
            public Stopwatch Sent = new Stopwatch();
            public long LastMs;
            public bool First;
            public SynthesizeOptions Options;
            public SynchronizationContext Context;
        }

        readonly object gate = new object();
        Task tail = Task.CompletedTask;
        LokutorSocket socket;
        Request current;
        CancellationTokenSource cancelled = new CancellationTokenSource();
        int use;                       // grows with every request, so an idle timer knows it is stale

        public TTSClient(string apiKey = null) { ApiKey = apiKey; }

        /// <summary>
        /// Speaks a text; done when all of it has arrived. The callbacks in the options run as it is generated, on the
        /// thread that called this (in Unity, the main one). Requests run one after another.
        /// </summary>
        public Task<TTSResult> SynthesizeAsync(string text, SynthesizeOptions options = null, CancellationToken cancel = default)
        {
            var request = new Request { Options = options ?? new SynthesizeOptions(), Context = SynchronizationContext.Current };
            var done = new TaskCompletionSource<TTSResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task previous;
            CancellationTokenSource both;
            lock (gate)
            {
                previous = tail;
                tail = done.Task;
                both = CancellationTokenSource.CreateLinkedTokenSource(cancelled.Token, cancel);
            }
            Run(previous, text, request, both, done);
            return done.Task;
        }

        async void Run(Task previous, string text, Request r, CancellationTokenSource cancel, TaskCompletionSource<TTSResult> done)
        {
            LokutorError error;
            try
            {
                await previous.ConfigureAwait(false);
                error = await SpeakAsync(text, r, cancel.Token).ConfigureAwait(false);
                // A request that failed leaves its connection in an unknown state: what it still had to say would be
                // taken for the next one's answer.
                if (error != null && r.Sent.IsRunning) await DropSocket().ConfigureAwait(false);
            }
            catch (Exception e) { error = new LokutorError("internal.error", e.Message, e.GetType().Name); }
            cancel.Dispose();
            int mine;
            lock (gate)
            {
                current = null;
                mine = ++use;
            }
            CloseWhenIdle(mine);
            TTSResult result;
            lock (r) result = new TTSResult { Audio = r.Pcm.ToArray(), Visemes = r.Visemes, Error = error };
            done.TrySetResult(result);
            if (error != null && r.Options.OnError != null) Call(r, () => r.Options.OnError(error));
        }

        async Task<LokutorError> SpeakAsync(string text, Request r, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested) return new LokutorError("internal.cancelled", "Cancelled.");
            lock (gate)
            {
                use++;
                current = r;
            }
            using (cancel.Register(() => r.Done.TrySetResult(new LokutorError("internal.cancelled", "Cancelled."))))
            {
                var error = await Open(r, cancel).ConfigureAwait(false);
                if (error != null) return error;
                r.Sent.Start();
                Send(MiniJson.Serialize(Body(text, r.Options)), r);
                return await Watch(r).ConfigureAwait(false);
            }
        }

        static Dictionary<string, object> Body(string text, SynthesizeOptions o)
        {
            var d = new Dictionary<string, object> { ["text"] = text, ["voice"] = o.Voice, ["language"] = o.Language, ["visemes"] = o.Visemes };
            if (o.Speed.HasValue) d["speed"] = o.Speed.Value;
            if (o.Steps.HasValue) d["steps"] = o.Steps.Value;
            if (o.Temperature.HasValue) d["temperature"] = o.Temperature.Value;
            if (o.DictionaryIds != null) d["dictionary_ids"] = o.DictionaryIds;
            if (o.Pronunciations != null) d["pronunciations"] = o.Pronunciations;
            return d;
        }

        async Task<LokutorError> Open(Request r, CancellationToken cancel)
        {
            lock (gate)
                if (socket != null && socket.IsOpen) return null;
            // Your auth callback runs on the thread the request came from.
            var auth = Auth;
            if (auth != null && r.Context != null && SynchronizationContext.Current != r.Context) auth = () => OnContext(r.Context, Auth);
            var opened = await LokutorSocket.OpenAsync(ServerUrl, ApiKey, auth, null, UserAgent, ConnectTimeout, cancel).ConfigureAwait(false);
            if (opened.Error != null) return opened.Error;
            var s = opened.Socket;
            s.OnText = t => { if (Current(s) is Request now) OnText(now, t); };
            s.OnBinary = b => { if (Current(s) is Request now) OnAudio(now, b); };
            s.OnClosed = (code, reason) => OnClosed(s, code, reason);
            lock (gate) socket = s;
            s.Start();
            return null;
        }

        Request Current(LokutorSocket s)
        {
            lock (gate) return socket == s ? current : null;
        }

        static Task<string> OnContext(SynchronizationContext context, Func<Task<string>> f)
        {
            var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(async _ =>
            {
                try { done.TrySetResult(await f()); }
                catch (Exception e) { done.TrySetException(e); }
            }, null);
            return done.Task;
        }

        void Send(string json, Request r)
        {
            LokutorSocket s;
            lock (gate) s = socket;
            if (s != null) s.SendText(json);
            else r.Done.TrySetResult(new LokutorError("ws.close", "The connection was lost.", "", true));
        }

        async Task<LokutorError> Watch(Request r)
        {
            while (true)
            {
                var limit = r.First ? StallTimeout : FirstAudioTimeout;
                var left = limit - (r.Sent.Elapsed - TimeSpan.FromMilliseconds(Interlocked.Read(ref r.LastMs)));
                if (left <= TimeSpan.Zero)
                    r.Done.TrySetResult(new LokutorError("internal.timeout", r.First ? "The stream stalled." : "No audio within " + (int)FirstAudioTimeout.TotalSeconds + " s.", "", true));
                var done = r.Done.Task;
                if (done.IsCompleted || await Task.WhenAny(done, Task.Delay(left + TimeSpan.FromMilliseconds(5))).ConfigureAwait(false) == done)
                    return await done.ConfigureAwait(false);
            }
        }

        static void OnText(Request r, string text)
        {
            if (text == "EOS") { r.Done.TrySetResult(null); return; }
            object parsed;
            try { parsed = MiniJson.Parse(text); } catch (FormatException) { return; }
            if (parsed is List<object>) { OnVisemes(r, parsed); return; }
            if (!(parsed is Dictionary<string, object> msg)) return;
            msg.TryGetValue("data", out var data);
            switch (Json.Str(msg, "type"))
            {
                case "error": r.Done.TrySetResult(LokutorError.FromServer(data)); break;
                case "audio": if (data is string b64) OnAudio(r, Convert.FromBase64String(b64)); break;
                case "visemes": OnVisemes(r, data); break;
                case "eos": r.Done.TrySetResult(null); break;
            }
        }

        static void OnAudio(Request r, byte[] pcm)
        {
            if (r.Done.Task.IsCompleted || pcm.Length == 0) return;
            long now = r.Sent.ElapsedMilliseconds;
            Interlocked.Exchange(ref r.LastMs, now);
            if (!r.First)
            {
                r.First = true;
                if (r.Options.OnTTFB != null) Call(r, () => r.Options.OnTTFB((int)now));
            }
            lock (r) r.Pcm.Write(pcm, 0, pcm.Length);
            if (r.Options.OnAudio != null) Call(r, () => r.Options.OnAudio(pcm));
        }

        static void OnVisemes(Request r, object payload)
        {
            if (r.Done.Task.IsCompleted) return;
            var list = Viseme.FromServer(payload);
            lock (r) r.Visemes.AddRange(list);
            if (r.Options.OnVisemes != null) Call(r, () => r.Options.OnVisemes(list));
        }

        static void Call(Request r, Action a)
        {
            if (r.Context != null) r.Context.Post(_ => a(), null);
            else a();
        }

        void OnClosed(LokutorSocket s, int code, string reason)
        {
            Request r;
            lock (gate)
            {
                if (socket != s) return;
                socket = null;
                r = current;
            }
            if (r == null) return;
            if (r.First && code == 1000) r.Done.TrySetResult(null);
            else r.Done.TrySetResult(new LokutorError("internal.error", "The connection closed (" + code + (string.IsNullOrEmpty(reason) ? "" : ": " + reason) + ").", "", true));
        }

        async Task DropSocket()
        {
            LokutorSocket s;
            lock (gate)
            {
                s = socket;
                socket = null;
            }
            if (s != null) await s.CloseAsync().ConfigureAwait(false);
        }

        async void CloseWhenIdle(int mine)
        {
            await Task.Delay(KeepOpen).ConfigureAwait(false);
            lock (gate)
                if (use != mine || current != null) return;
            await DropSocket().ConfigureAwait(false);
        }

        /// <summary>Stops the request being spoken and forgets the ones waiting.</summary>
        public void Cancel()
        {
            CancellationTokenSource old;
            lock (gate)
            {
                old = cancelled;
                cancelled = new CancellationTokenSource();
            }
            old.Cancel();
        }

        /// <summary>Cancels, and closes the connection.</summary>
        public void Close()
        {
            Cancel();
            _ = DropSocket();
        }

        public void Dispose() => Close();
    }
}
