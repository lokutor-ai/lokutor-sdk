using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lokutor
{
    /// <summary>
    /// One mouth shape of a reply: V its id (0-14) and C its name in LokutorApi.Visemes, T when it starts and D how long
    /// it lasts, in seconds from the reply's first sample.
    /// </summary>
    public struct Viseme
    {
        public int V;
        public string C;
        public float T;
        public float D;

        public Viseme(int v, float t, float d)
        {
            V = v;
            C = v >= 0 && v < LokutorApi.Visemes.Count ? LokutorApi.Visemes[v] : "sil";
            T = t;
            D = d;
        }

        public override string ToString() => C + "@" + T.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Reads what the server sends. A missing duration lasts until the next one starts.</summary>
        internal static List<Viseme> FromServer(object payload)
        {
            var list = new List<Viseme>();
            if (payload is Dictionary<string, object> one)
                payload = one.TryGetValue("visemes", out var inner) && inner is List<object> ? inner : new List<object> { one };
            if (!(payload is List<object> items)) return list;
            foreach (var item in items)
            {
                if (!(item is Dictionary<string, object> d) || !(d.ContainsKey("t") || d.ContainsKey("timestamp"))) continue;
                string c = Json.Str(d, "c", Json.Str(d, "char", "sil"));
                int v = d.ContainsKey("v") ? Json.Int(d, "v", -1) : Json.Int(d, "id", -1);
                if (v < 0 || v > 14) v = LokutorApi.VisemeId(c);
                list.Add(new Viseme { V = v, C = c, T = (float)Json.Num(d, "t", Json.Num(d, "timestamp")), D = (float)Json.Num(d, "d") });
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].D > 0f) continue;
                var e = list[i];
                e.D = i + 1 < list.Count ? Math.Max(0.02f, list[i + 1].T - e.T) : 0.12f;
                list[i] = e;
            }
            return list;
        }
    }
}
