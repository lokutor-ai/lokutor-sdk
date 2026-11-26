using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    /// <summary>One WebSocket connection: sends in order from any thread, hands over whole messages.</summary>
    sealed class LokutorSocket
    {
        // 10 s of 20 ms microphone blocks: on a stalled network the oldest are dropped, not the newest.
        const int MaxQueuedAudio = 500;

        readonly ClientWebSocket ws;
        readonly CancellationTokenSource life = new CancellationTokenSource();
        readonly ConcurrentQueue<string> textOut = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<byte[]> audioOut = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim work = new SemaphoreSlim(0);
        Task receiving;
        int ended;

        public Action<string> OnText;
        public Action<byte[]> OnBinary;
        /// <summary>The connection is over (not called after CloseAsync): close code, reason.</summary>
        public Action<int, string> OnClosed;

        LokutorSocket(ClientWebSocket ws) { this.ws = ws; }

        public bool IsOpen => Volatile.Read(ref ended) == 0 && ws.State == WebSocketState.Open;

        public sealed class Opened
        {
            public LokutorSocket Socket;
            public LokutorError Error;
        }

        /// <summary>Connects with a key (or the ticket auth returns). Exactly one of Socket and Error is set.</summary>
        public static async Task<Opened> OpenAsync(string url, string apiKey, Func<Task<string>> auth, IEnumerable<string> query,
                                                   string userAgent, TimeSpan timeout, CancellationToken cancel)
        {
            string key = apiKey;
            bool ticket = auth != null;
            if (ticket)
            {
                try { key = await auth().ConfigureAwait(false); }
                catch (Exception e) { return new Opened { Error = new LokutorError("auth.missing_key", "The auth callback failed.", e.Message, true) }; }
                if (string.IsNullOrEmpty(key)) return new Opened { Error = new LokutorError("auth.missing_key", "The auth callback returned no credential.", "", true) };
            }
            else if (string.IsNullOrEmpty(key)) key = Environment.GetEnvironmentVariable("LOKUTOR_API_KEY");
            if (string.IsNullOrEmpty(key))
                return new Opened { Error = new LokutorError("auth.missing_key", "No API key: set ApiKey, or Auth for a shipped game.") };

            var parts = new List<string>();
            if (query != null) parts.AddRange(query);
            var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("User-Agent", userAgent);
            // Tickets are made for the query; a key stays out of URLs, which get logged.
            if (ticket || key.StartsWith("wst_", StringComparison.Ordinal)) parts.Add("api_key=" + Uri.EscapeDataString(key));
            else ws.Options.SetRequestHeader("X-API-Key", key);
            if (parts.Count > 0) url += (url.Contains("?") ? "&" : "?") + string.Join("&", parts);

            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                limit.CancelAfter(timeout);
                try
                {
                    await ws.ConnectAsync(new Uri(url), limit.Token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    ws.Dispose();
                    if (cancel.IsCancellationRequested) return new Opened { Error = new LokutorError("internal.cancelled", "Closed while connecting.") };
                    if (limit.IsCancellationRequested)
                        return new Opened { Error = new LokutorError("internal.timeout", "Could not reach Lokutor in " + (int)timeout.TotalSeconds + " s.") };
                    string why = e.InnerException != null && e.InnerException.Message != e.Message ? e.Message + " " + e.InnerException.Message : e.Message;
                    // Sockets do not say why they were refused, and the server answers the same request over HTTP with the
                    // reason: a key that is wrong, a balance that is empty, an agent that does not exist.
                    string http = url.Replace("wss://", "https://").Replace("ws://", "http://");
                    var reply = await LokutorApi.RequestJsonAsync(HttpMethod.Get, http, ticket || key.StartsWith("wst_", StringComparison.Ordinal) ? null : key, null, TimeSpan.FromSeconds(8), cancel).ConfigureAwait(false);
                    if (cancel.IsCancellationRequested) return new Opened { Error = new LokutorError("internal.cancelled", "Closed while connecting.") };
                    if (reply.Status >= 400 && reply.Body.ContainsKey("code")) return new Opened { Error = reply.Error };
                    return new Opened { Error = new LokutorError("ws.close", "Could not connect: check the API key and the network.", why, true) };
                }
            }
            return new Opened { Socket = new LokutorSocket(ws) };
        }

        /// <summary>Begins sending and receiving. Set the callbacks first.</summary>
        public void Start()
        {
            Task.Run(SendLoop);
            receiving = Task.Run(ReceiveLoop);
        }

        public void SendText(string text)
        {
            textOut.Enqueue(text);
            work.Release();
        }

        public void SendBinary(byte[] data)
        {
            if (audioOut.Count >= MaxQueuedAudio) audioOut.TryDequeue(out _);
            audioOut.Enqueue(data);
            work.Release();
        }

        async Task SendLoop()
        {
            try
            {
                while (true)
                {
                    await work.WaitAsync(life.Token).ConfigureAwait(false);
                    // Text first: configuration and tool results must not wait behind audio.
                    if (textOut.TryDequeue(out var text))
                        await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, life.Token).ConfigureAwait(false);
                    else if (audioOut.TryDequeue(out var audio))
                        await ws.SendAsync(new ArraySegment<byte>(audio), WebSocketMessageType.Binary, true, life.Token).ConfigureAwait(false);
                }
            }
            catch (Exception) { /* the receive loop reports why the connection ended */ }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            int code = 1006;
            string reason = "";
            try
            {
                while (true)
                {
                    var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), life.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        code = ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : 1005;
                        reason = ws.CloseStatusDescription ?? "";
                        break;
                    }
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    var data = message.ToArray();
                    message.SetLength(0);
                    try
                    {
                        if (r.MessageType == WebSocketMessageType.Binary) OnBinary?.Invoke(data);
                        else OnText?.Invoke(Encoding.UTF8.GetString(data));
                    }
                    catch (Exception) { /* one bad message must not end the conversation */ }
                }
                if (ws.State == WebSocketState.CloseReceived)
                {
                    using (var t = new CancellationTokenSource(1000))
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", t.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) { if (reason.Length == 0) reason = e.Message; }
            if (Interlocked.Exchange(ref ended, 1) != 0) return;       // CloseAsync got there first
            life.Cancel();
            ws.Dispose();
            OnClosed?.Invoke(code, reason);
        }

        /// <summary>Says goodbye and lets go of the connection. OnClosed is not called.</summary>
        public async Task CloseAsync()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0) return;
            OnText = null;
            OnBinary = null;
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (var t = new CancellationTokenSource(2000))
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", t.Token).ConfigureAwait(false);
                    if (receiving != null) await Task.WhenAny(receiving, Task.Delay(1000)).ConfigureAwait(false);
                }
            }
            catch (Exception) { }
            life.Cancel();
            ws.Dispose();
        }
    }
}
