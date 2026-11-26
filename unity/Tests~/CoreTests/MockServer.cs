// A Lokutor server for the tests, in this process: the WebSocket endpoints and the HTTP ones, on one local port.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lokutor;

sealed class MockServer : IDisposable
{
    public sealed class Session
    {
        public string Url;
        public Dictionary<string, string> Headers;
        /// <summary>What the client sent: JSON objects, and the bytes of its audio.</summary>
        public List<Dictionary<string, object>> Text { get { lock (this) return text.ToList(); } }
        public readonly List<byte> Audio = new List<byte>();
        internal readonly List<Dictionary<string, object>> text = new List<Dictionary<string, object>>();
        internal WebSocket Ws;
        internal TcpClient Tcp;
        readonly SemaphoreSlim sending = new SemaphoreSlim(1);

        public List<string> Types() { lock (this) return text.Select(m => m.TryGetValue("type", out var t) ? (string)t : "").ToList(); }

        /// <summary>The last message of a type, or null.</summary>
        public Dictionary<string, object> Last(string type)
        {
            lock (this) return text.LastOrDefault(m => m.TryGetValue("type", out var t) && (string)t == type);
        }

        public object Data(string type) => Last(type)?["data"];
        public int AudioBytes { get { lock (this) return Audio.Count; } }
        public short Sample(int i) { lock (this) return (short)(Audio[2 * i] | Audio[2 * i + 1] << 8); }
        public void ClearAudio() { lock (this) Audio.Clear(); }

        public void Send(string text) => Send(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text);
        public void Send(byte[] binary) => Send(binary, WebSocketMessageType.Binary);

        public void Audio16(byte[] pcm, int generation) =>
            Send("{\"type\":\"audio\",\"generation\":" + generation + ",\"data\":\"" + Convert.ToBase64String(pcm) + "\"}");

        void Send(byte[] data, WebSocketMessageType type)
        {
            sending.Wait();
            try { Ws.SendAsync(new ArraySegment<byte>(data), type, true, CancellationToken.None).Wait(); }
            catch (AggregateException) { }
            finally { sending.Release(); }
        }

        public void Close(int code, string reason) =>
            Ws.CloseOutputAsync((WebSocketCloseStatus)code, reason, CancellationToken.None).Wait();

        /// <summary>The network goes away.</summary>
        public void Drop()
        {
            Tcp.LingerState = new LingerOption(true, 0);
            Tcp.Close();
        }
    }

    public sealed class HttpRequest
    {
        public string Line;
        public Dictionary<string, string> Headers;
        public Dictionary<string, object> Body;
    }

    public readonly List<Session> Sessions = new List<Session>();
    public readonly List<HttpRequest> HttpRequests = new List<HttpRequest>();
    /// <summary>Called with each JSON message a client sends.</summary>
    public Action<Session, Dictionary<string, object>> OnMessage;
    /// <summary>Turns every client away.</summary>
    public volatile bool Refuse;
    /// <summary>What it answers then.</summary>
    public string RefuseWith = "";
    public int HttpStatus = 200;
    public string HttpBody = "{}";

    readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);

    public MockServer()
    {
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }
                _ = Task.Run(() => Serve(c));
            }
        });
    }

    int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string Url(string path) => "ws://127.0.0.1:" + Port + path;

    public int Count { get { lock (Sessions) return Sessions.Count; } }
    public Session Current { get { lock (Sessions) return Sessions.LastOrDefault(); } }

    public void Dispose()
    {
        listener.Stop();
        lock (Sessions)
            foreach (var s in Sessions) s.Tcp.Close();
    }

    async Task Serve(TcpClient tcp)
    {
        try
        {
            var stream = tcp.GetStream();
            string head = await ReadHead(stream);
            if (head == null) return;
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in lines.Skip(1))
            {
                int colon = l.IndexOf(':');
                if (colon > 0) headers[l.Substring(0, colon)] = l.Substring(colon + 1).Trim();
            }
            if (Refuse)
            {
                await Write(stream, "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(RefuseWith) + "\r\nConnection: close\r\n\r\n" + RefuseWith);
                tcp.Close();
                return;
            }
            if (headers.TryGetValue("Sec-WebSocket-Key", out var key)) await ServeSocket(tcp, stream, lines[0].Split(' ')[1], headers, key);
            else await ServeHttp(tcp, stream, lines[0], headers);
        }
        catch (Exception) { tcp.Close(); }
    }

    static async Task<string> ReadHead(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 4 || bytes[^1] != '\n' || bytes[^2] != '\r' || bytes[^3] != '\n' || bytes[^4] != '\r')
        {
            if (await stream.ReadAsync(one, 0, 1) == 0) return null;
            bytes.Add(one[0]);
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    static Task Write(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return stream.WriteAsync(bytes, 0, bytes.Length);
    }

    async Task ServeHttp(TcpClient tcp, Stream stream, string line, Dictionary<string, string> headers)
    {
        var body = new byte[headers.TryGetValue("Content-Length", out var n) ? int.Parse(n) : 0];
        for (int got = 0; got < body.Length;) got += await stream.ReadAsync(body, got, body.Length - got);
        lock (HttpRequests)
            HttpRequests.Add(new HttpRequest
            {
                Line = line, Headers = headers, Body = body.Length > 0 ? MiniJson.Parse(Encoding.UTF8.GetString(body)) as Dictionary<string, object> : null,
            });
        await Write(stream, "HTTP/1.1 " + HttpStatus + " Status\r\nContent-Type: application/json\r\nContent-Length: " + Encoding.UTF8.GetByteCount(HttpBody) + "\r\nConnection: close\r\n\r\n" + HttpBody);
        tcp.Close();
    }

    async Task ServeSocket(TcpClient tcp, Stream stream, string path, Dictionary<string, string> headers, string key)
    {
        string accept;
        using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await Write(stream, "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
        var s = new Session { Url = path, Headers = headers, Tcp = tcp, Ws = WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(30)) };
        lock (Sessions) Sessions.Add(s);
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        while (true)
        {
            var r = await s.Ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                if (s.Ws.State == WebSocketState.CloseReceived) await s.Ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                return;
            }
            message.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;
            var data = message.ToArray();
            message.SetLength(0);
            if (r.MessageType == WebSocketMessageType.Binary)
            {
                lock (s) s.Audio.AddRange(data);
                continue;
            }
            var msg = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(data));
            lock (s) s.text.Add(msg);
            OnMessage?.Invoke(s, msg);
        }
    }
}
