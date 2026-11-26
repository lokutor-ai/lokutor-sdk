// Constants and helpers shared by the whole package, and the API's public discovery endpoints (no key needed). The same
// values as the JavaScript and Python SDKs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lokutor
{
    public static class LokutorApi
    {
        public const string Version = "0.2.0";

        public const string VoiceAgentUrl = "wss://api.lokutor.com/ws/agent";
        public const string TTSUrl = "wss://api.lokutor.com/ws/tts";
        public const string STTUrl = "wss://api.lokutor.com/ws/stt";

        /// <summary>Rates of the audio the API takes and sends (16-bit PCM, mono).</summary>
        public const int InputRate = 16000, OutputRate = 44100;

        /// <summary>Pass as a voice agent's language so the character follows the language the player speaks.</summary>
        public const string AutoLanguage = "auto";

        /// <summary>Voice slots: each is a native speaker of the language in use. GetVoicesAsync has the live list.</summary>
        public static readonly IReadOnlyList<string> Voices = new[] { "F1", "F2", "F3", "F4", "F5", "M1", "M2", "M3", "M4", "M5" };

        /// <summary>Languages the API documents for SDKs. GetLanguagesAsync has the live list.</summary>
        public static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>
        {
            ["en"] = "English", ["es"] = "Spanish", ["ca"] = "Catalan", ["gl"] = "Galician", ["eu"] = "Basque",
            ["pt"] = "Portuguese", ["fr"] = "French", ["it"] = "Italian", ["de"] = "German",
        };

        /// <summary>The 15 mouth shapes (the Oculus / MPEG-4 set), by id. Viseme events carry the id (V) and the name (C).</summary>
        public static readonly IReadOnlyList<string> Visemes = new[]
            { "sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "ih", "oh", "ou" };

        /// <summary>
        /// Close codes after which a session is opened again: the connection dropped or the server went away. Every
        /// other close is the server ending the session on purpose.
        /// </summary>
        public static readonly IReadOnlyCollection<int> ReconnectableCloseCodes = new HashSet<int> { 1001, 1006, 1011, 1012, 1013, 1014 };

        internal static bool Reconnectable(int code) => ((HashSet<int>)ReconnectableCloseCodes).Contains(code);

        static readonly Regex Tags = new Regex(@"\s*\[(?:breath|laugh|sigh|happy|sad|angry|surprised|fearful|disgusted|excited|anxious)\]\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The value the protocol carries for a language: "auto" and "" are both "" (follow the player).</summary>
        public static string LanguageToWire(string language) => language == null || language == AutoLanguage ? "" : language;

        /// <summary>A line without the expression tags the voice performs ([laugh], [sad]...), for subtitles.</summary>
        public static string StripTags(string text) => string.IsNullOrEmpty(text) ? "" : Tags.Replace(text, " ").Trim();

        /// <summary>The id (0-14) of a viseme name, whatever its case; 0 (closed) for an unknown one.</summary>
        public static int VisemeId(string name)
        {
            for (int i = 0; i < Visemes.Count; i++)
                if (string.Equals(Visemes[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>ws://host/path -> http://host</summary>
        public static string HttpOrigin(string url)
        {
            string http = url.Replace("wss://", "https://").Replace("ws://", "http://");
            int slash = http.IndexOf('/', http.IndexOf("://", StringComparison.Ordinal) + 3);
            return slash < 0 ? http : http.Substring(0, slash);
        }

        // ---- Discovery (these throw LokutorException when the API cannot be reached) ---------------

        public static async Task<List<VoiceInfo>> GetVoicesAsync(string baseUrl = VoiceAgentUrl)
        {
            var list = new List<VoiceInfo>();
            foreach (var d in Json.Objects(await GetAsync(baseUrl, "/voices").ConfigureAwait(false), "voices"))
                list.Add(new VoiceInfo { Id = Json.Str(d, "id"), Name = Json.Str(d, "name"), Gender = Json.Str(d, "gender"), Languages = Json.Strings(d, "languages") });
            return list;
        }

        public static async Task<List<LanguageInfo>> GetLanguagesAsync(string baseUrl = VoiceAgentUrl)
        {
            var list = new List<LanguageInfo>();
            foreach (var d in Json.Objects(await GetAsync(baseUrl, "/languages").ConfigureAwait(false), "languages"))
                list.Add(new LanguageInfo { Code = Json.Str(d, "code"), Name = Json.Str(d, "name") });
            return list;
        }

        public static async Task<List<ModelInfo>> GetModelsAsync(string baseUrl = VoiceAgentUrl)
        {
            var list = new List<ModelInfo>();
            foreach (var d in Json.Objects(await GetAsync(baseUrl, "/models").ConfigureAwait(false), "models"))
                list.Add(new ModelInfo { Name = Json.Str(d, "name"), Description = Json.Str(d, "description"), IsDefault = Json.Bool(d, "default") });
            return list;
        }

        /// <summary>Request limits and defaults.</summary>
        public static async Task<ServerConfig> GetConfigAsync(string baseUrl = VoiceAgentUrl)
        {
            var d = await GetAsync(baseUrl, "/config").ConfigureAwait(false);
            return new ServerConfig
            {
                MaxTextLength = Json.Int(d, "max_text_length"), MinSpeed = Json.Num(d, "min_speed"), MaxSpeed = Json.Num(d, "max_speed"),
                MinSteps = Json.Int(d, "min_steps"), MaxSteps = Json.Int(d, "max_steps"),
                SampleRate = Json.Int(d, "sample_rate"), Channels = Json.Int(d, "channels"),
            };
        }

        /// <summary>Runtime status of the service, as the API reports it.</summary>
        public static Task<Dictionary<string, object>> GetStatusAsync(string baseUrl = VoiceAgentUrl) => GetAsync(baseUrl, "/status");

        /// <summary>Whether the service is ready to take requests.</summary>
        public static async Task<HealthStatus> GetHealthAsync(string baseUrl = VoiceAgentUrl)
        {
            var d = await GetAsync(baseUrl, "/ready").ConfigureAwait(false);
            return new HealthStatus { Ready = Json.Bool(d, "ready"), Status = Json.Str(d, "status") };
        }

        static async Task<Dictionary<string, object>> GetAsync(string baseUrl, string path)
        {
            var reply = await RequestJsonAsync(HttpMethod.Get, HttpOrigin(baseUrl) + path, null, null, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            if (reply.Error != null) throw new LokutorException(reply.Error);
            return reply.Body;
        }

        // ---- HTTP ----------------------------------------------------------------------------------

        static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        internal sealed class JsonReply
        {
            public int Status;
            public Dictionary<string, object> Body = new Dictionary<string, object>();
            public LokutorError Error;
        }

        internal static async Task<JsonReply> RequestJsonAsync(HttpMethod method, string url, string apiKey, string body, TimeSpan timeout, CancellationToken cancel = default)
        {
            var reply = new JsonReply();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using (var request = new HttpRequestMessage(method, url))
            {
                cts.CancelAfter(timeout);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                if (!string.IsNullOrEmpty(apiKey)) request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                string text;
                try
                {
                    using (var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false))
                    {
                        reply.Status = (int)response.StatusCode;
                        text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    reply.Error = cancel.IsCancellationRequested
                        ? new LokutorError("internal.cancelled", "Cancelled.")
                        : new LokutorError("internal.timeout", "No answer from " + url + " in " + (int)timeout.TotalSeconds + " s.");
                    return reply;
                }
                catch (Exception e)
                {
                    reply.Error = new LokutorError("internal.error", "The request to " + url + " failed.", e.Message, true);
                    return reply;
                }
                object parsed = null;
                try { parsed = MiniJson.Parse(text); } catch (FormatException) { }
                if (parsed is Dictionary<string, object> d) reply.Body = d;
                if (reply.Status < 200 || reply.Status >= 300)
                    reply.Error = reply.Body.ContainsKey("code")
                        ? LokutorError.FromServer(reply.Body)
                        : new LokutorError("internal.error", "HTTP " + reply.Status + " from " + url, text.Length > 300 ? text.Substring(0, 300) : text, reply.Status >= 500);
                return reply;
            }
        }
    }

    public sealed class VoiceInfo { public string Id, Name, Gender; public List<string> Languages = new List<string>(); }
    public sealed class LanguageInfo { public string Code, Name; }
    public sealed class ModelInfo { public string Name, Description; public bool IsDefault; }
    public sealed class HealthStatus { public bool Ready; public string Status; }

    public sealed class ServerConfig
    {
        public int MaxTextLength, MinSteps, MaxSteps, SampleRate, Channels;
        public double MinSpeed, MaxSpeed;
    }

    /// <summary>Reading parsed JSON without caring whether a field is there.</summary>
    static class Json
    {
        public static string Str(Dictionary<string, object> d, string k, string fallback = "") =>
            d.TryGetValue(k, out var v) && v != null ? v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;

        public static double Num(Dictionary<string, object> d, string k, double fallback = 0) => d.TryGetValue(k, out var v) && v is double x ? x : fallback;

        public static int Int(Dictionary<string, object> d, string k, int fallback = 0) => (int)Num(d, k, fallback);

        public static bool Bool(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is bool b && b;

        public static IEnumerable<Dictionary<string, object>> Objects(Dictionary<string, object> d, string k)
        {
            if (d.TryGetValue(k, out var v) && v is List<object> list)
                foreach (var o in list)
                    if (o is Dictionary<string, object> x) yield return x;
        }

        public static List<string> Strings(Dictionary<string, object> d, string k)
        {
            var found = new List<string>();
            if (d.TryGetValue(k, out var v) && v is List<object> list)
                foreach (var o in list)
                    if (o is string s) found.Add(s);
            return found;
        }

        public static Dictionary<string, object> Msg(string type, object data) => new Dictionary<string, object> { ["type"] = type, ["data"] = data };
    }
}
