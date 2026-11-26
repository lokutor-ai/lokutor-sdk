// Lip sync without an engine: the mouth's timeline, and which blend shapes of a face make each mouth shape. LokutorLipSync
// (Unity) drives meshes, jaws and sprites with it.
using System;
using System.Collections.Generic;

namespace Lokutor
{
    /// <summary>
    /// The shape of a mouth over time. Give it the visemes of a reply and, every frame, how much of the reply has been
    /// heard: it answers with the weight of each of the 15 shapes. Without visemes it follows the loudness.
    /// </summary>
    public sealed class LipSync
    {
        /// <summary>How far the mouth opens.</summary>
        public float Intensity = 1f;
        /// <summary>Seconds the mouth leads the sound: people open before they speak.</summary>
        public float Lead = 0.05f;
        /// <summary>Seconds over which one shape melts into the next. 0.05 is crisp and cartoonish, 0.12 soft.</summary>
        public float Blend = 0.07f;
        /// <summary>How much quiet speech closes the mouth, 0..1.</summary>
        public float Loudness = 0.35f;
        /// <summary>Steps per second of the mouth, for a hand-drawn look (12 = animation on twos). 0 = smooth.</summary>
        public float SteppedFps;
        /// <summary>Loudness multiplier for mouths that follow the loudness (no visemes).</summary>
        public float Gain = 1.5f;

        /// <summary>The mouth changed shape: its name in LokutorApi.Visemes.</summary>
        public event Action<string> VisemeChanged;

        /// <summary>Weight of each of the 15 visemes right now (LokutorApi.Visemes order).</summary>
        public float[] Weights { get; } = new float[15];
        /// <summary>The strongest one.</summary>
        public string Current { get; private set; } = "sil";
        /// <summary>How open the mouth is, 0..1.</summary>
        public float MouthOpen { get; private set; }
        public bool HasVisemes => events.Count > 0;

        /// <summary>How open each viseme is, for jaws and mouth-open shapes.</summary>
        public static readonly IReadOnlyList<float> Openness = new[]
            { 0f, 0f, 0.12f, 0.28f, 0.3f, 0.38f, 0.3f, 0.16f, 0.28f, 0.3f, 1f, 0.62f, 0.36f, 0.72f, 0.34f };

        readonly List<Viseme> events = new List<Viseme>();
        readonly float[] target = new float[15];
        int first;
        float sinceStep;

        /// <summary>The visemes of the reply being heard, as they arrive.</summary>
        public void PushVisemes(IEnumerable<Viseme> list)
        {
            foreach (var v in list)
                if (v.V >= 0 && v.V < 15) events.Add(v);
        }

        /// <summary>A new reply, or silence: the mouth closes.</summary>
        public void Clear()
        {
            events.Clear();
            first = 0;
        }

        /// <summary>
        /// One frame of the mouth. Heard is how much of the reply has been heard, in seconds; level its loudness (RMS);
        /// speaking whether it is playing at all. False: nothing changed (a stepped mouth holds its pose).
        /// </summary>
        public bool Advance(float heard, float level, bool speaking, float delta)
        {
            if (SteppedFps > 0f)
            {
                sinceStep += delta;
                if (sinceStep < 1f / SteppedFps) return false;
                delta = sinceStep;
                sinceStep = 0f;
            }
            Array.Clear(target, 0, 15);
            if (speaking && events.Count > 0)
            {
                At(heard + Lead);
                // Quiet syllables open less; the lips still close fully on p, b, m.
                float volume = 1f + (Clamp01(level * 9f) - 1f) * Loudness;
                for (int v = 2; v < 15; v++) target[v] *= volume;
            }
            else if (speaking) target[10] = Clamp01(level * 4f * Gain);
            // The shapes already cross-fade on the timeline: this only takes the edge off changes of loudness and cuts.
            float k = 1f - (float)Math.Exp(-delta / Math.Max(0.001f, Blend * (events.Count > 0 ? 0.35f : 0.6f)));
            int strongest = 0;
            float open = 0f;
            for (int v = 0; v < 15; v++)
            {
                Weights[v] += (target[v] - Weights[v]) * k;
                open += Weights[v] * Openness[v];
                if (v > 0 && Weights[v] > Math.Max(0.2f, strongest > 0 ? Weights[strongest] : 0f)) strongest = v;
            }
            MouthOpen = Clamp01(open * Intensity);
            if (LokutorApi.Visemes[strongest] != Current)
            {
                Current = LokutorApi.Visemes[strongest];
                VisemeChanged?.Invoke(Current);
            }
            return true;
        }

        // Each shape rises over Blend around its start and falls over Blend around its end, so two neighbours overlap and
        // a short one between two others never reaches full weight: that is coarticulation.
        void At(float t)
        {
            float half = Blend * 0.5f;
            while (first < events.Count && events[first].T + events[first].D + half < t) first++;
            float total = 0f;
            for (int i = first; i < events.Count && events[i].T - half <= t; i++)
            {
                var e = events[i];
                float w = Math.Min(SmoothStep(e.T - half, e.T + half, t), 1f - SmoothStep(e.T + e.D - half, e.T + e.D + half, t));
                target[e.V] += w;
                total += w;
            }
            if (total > 1f)
                for (int v = 0; v < 15; v++) target[v] /= total;
            target[0] = 0f;          // sil is the rest pose, not a shape
        }

        /// <summary>The frame of a set of mouth drawings: 0 closed, 1 slightly open, 2 open, 3 round (if there are that many).</summary>
        public int SpriteFrame(int count)
        {
            int f = 0;
            if (Current == "oh" || Current == "ou" || Current == "RR" || Current == "CH") f = count > 3 ? 3 : 1;
            else if (MouthOpen > 0.45f) f = 2;
            else if (MouthOpen > 0.08f) f = 1;
            return Math.Max(0, Math.Min(f, count - 1));
        }

        static float SmoothStep(float a, float b, float x)
        {
            float t = Clamp01((x - a) / Math.Max(1e-6f, b - a));
            return t * t * (3f - 2f * t);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>The kinds of mouth LipSync finds in a face.</summary>
    public enum MouthRigKind { None, Visemes, Vrm, ArKit, MouthOpen, Custom }

    /// <summary>
    /// Which blend shapes of a face make each viseme. They are found by name: the 15 visemes of Oculus, Ready Player Me
    /// and VRChat (viseme_PP, viseme_aa...), VRM (aa ih ou ee oh) and the A I U E O of VRoid and MMD-style rigs, ARKit (jawOpen,
    /// mouthFunnel...), or a single mouth-open shape. Or write your own mapping.
    /// </summary>
    public sealed class MouthRig
    {
        public MouthRigKind Kind { get; private set; } = MouthRigKind.None;

        /// <summary>One blend shape that moves: its index, and how much of it each viseme takes.</summary>
        public struct Shape
        {
            public int Index;
            public float[] PerViseme;

            /// <summary>Its weight for these viseme weights, 0..1 (a little more with a high intensity).</summary>
            public float Weight(float[] visemes, float intensity)
            {
                float sum = 0f;
                for (int v = 0; v < 15; v++) sum += visemes[v] * PerViseme[v];
                return Math.Max(0f, Math.Min(1.5f, sum * intensity));
            }
        }

        public List<Shape> Shapes { get; } = new List<Shape>();

        // Rows follow LokutorApi.Visemes: "shape weight shape weight...".
        static readonly string[] VrmRows =
        {
            "", "", "ih .15", "aa .25", "aa .3", "aa .35", "ou .35 ih .2", "ih .45", "aa .25", "ou .3", "aa 1", "ee .9", "ih .9", "oh 1", "ou 1",
        };

        static readonly string[] ArKitRows =
        {
            "",
            "mouthClose .3 mouthPressLeft .5 mouthPressRight .5 mouthRollLower .4 mouthRollUpper .3",
            "mouthRollLower .6 mouthUpperUpLeft .3 mouthUpperUpRight .3 jawOpen .06",
            "jawOpen .16 tongueOut .35",
            "jawOpen .2 mouthLowerDownLeft .2 mouthLowerDownRight .2",
            "jawOpen .26 mouthStretchLeft .15 mouthStretchRight .15",
            "mouthFunnel .5 jawOpen .12",
            "jawOpen .06 mouthSmileLeft .3 mouthSmileRight .3 mouthStretchLeft .2 mouthStretchRight .2",
            "jawOpen .18",
            "mouthFunnel .3 jawOpen .16",
            "jawOpen .62 mouthLowerDownLeft .2 mouthLowerDownRight .2",
            "jawOpen .36 mouthSmileLeft .25 mouthSmileRight .25 mouthStretchLeft .2 mouthStretchRight .2",
            "jawOpen .2 mouthSmileLeft .45 mouthSmileRight .45",
            "jawOpen .42 mouthFunnel .6",
            "jawOpen .16 mouthPucker .8",
        };

        static readonly string[] VisemePrefixes = { "viseme_", "vis_", "v_", "viseme.", "vrc.v_", "" };
        // Prefixes of rigs whose mouths are the vowels a i u e o (VRoid, Unity-chan and the like).
        static readonly string[] VowelPrefixes = { "fcl_mth_", "mth_", "mouth_" };
        static readonly string[] MouthOpenNames = { "mouthopen", "mouth_open", "jawopen", "jaw_open", "mouth_a", "mth_a", "open", "a" };

        /// <summary>
        /// Finds the mouth in a list of blend shape names (their order gives the indices). Mapping, if not empty, is used
        /// instead of the usual names: viseme name -> blend shape name -> weight 0..1.
        /// </summary>
        public static MouthRig Find(IReadOnlyList<string> names, IReadOnlyDictionary<string, Dictionary<string, float>> mapping = null)
        {
            var rig = new MouthRig();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++) index[names[i]] = i;
            for (int i = 0; i < names.Count; i++)
            {
                // FBX exporters put the mesh or blend-shape node in front: "Face.viseme_aa", "blendShape1.MTH_A".
                int dot = names[i].LastIndexOf('.');
                string last = dot >= 0 ? names[i].Substring(dot + 1) : "";
                if (last.Length > 0 && !index.ContainsKey(last)) index[last] = i;
            }
            var table = rig.Table(index, mapping);
            if (table == null) return rig;
            var byShape = new SortedDictionary<int, float[]>();
            for (int v = 0; v < 15; v++)
                foreach (var entry in table[v])
                {
                    if (!index.TryGetValue(entry.Key, out int i)) continue;
                    if (!byShape.TryGetValue(i, out var weights)) byShape[i] = weights = new float[15];
                    weights[v] = entry.Value;
                }
            foreach (var s in byShape) rig.Shapes.Add(new Shape { Index = s.Key, PerViseme = s.Value });
            return rig;
        }

        Dictionary<string, float>[] Table(Dictionary<string, int> index, IReadOnlyDictionary<string, Dictionary<string, float>> mapping)
        {
            var table = new Dictionary<string, float>[15];
            if (mapping != null && mapping.Count > 0)
            {
                Kind = MouthRigKind.Custom;
                for (int v = 0; v < 15; v++)
                    table[v] = mapping.TryGetValue(LokutorApi.Visemes[v], out var row) ? row : new Dictionary<string, float>();
                return table;
            }
            foreach (var prefix in VisemePrefixes)
            {
                int found = 0;
                for (int v = 0; v < 15; v++)
                {
                    table[v] = new Dictionary<string, float>();
                    string name = prefix + LokutorApi.Visemes[v];
                    // Ready Player Me: viseme_I, viseme_O, viseme_U.
                    if (!index.ContainsKey(name) && v >= 12) name = prefix + "iou"[v - 12];
                    if (!index.ContainsKey(name)) continue;
                    table[v][name] = 1f;
                    found++;
                }
                if (found < 8) continue;
                Kind = MouthRigKind.Visemes;
                return table;
            }
            if (index.ContainsKey("aa") && index.ContainsKey("oh") && (index.ContainsKey("ee") || index.ContainsKey("e")))
            {
                Kind = MouthRigKind.Vrm;
                return Parse(VrmRows, n => n == "ee" && !index.ContainsKey("ee") ? "e" : n);
            }
            foreach (var p in VowelPrefixes)
                if (index.ContainsKey(p + "a") && index.ContainsKey(p + "i") && index.ContainsKey(p + "u") && index.ContainsKey(p + "o"))
                {
                    Kind = MouthRigKind.Vrm;
                    return Parse(VrmRows, n => p + (n == "aa" ? "a" : n == "ih" ? "i" : n == "ou" ? "u" : n == "ee" ? "e" : "o"));
                }
            if (index.ContainsKey("jawOpen") && index.ContainsKey("mouthFunnel"))
            {
                Kind = MouthRigKind.ArKit;
                return Parse(ArKitRows, n => n);
            }
            foreach (var n in MouthOpenNames)
            {
                if (!index.ContainsKey(n)) continue;
                Kind = MouthRigKind.MouthOpen;
                for (int v = 0; v < 15; v++) table[v] = new Dictionary<string, float> { [n] = LipSync.Openness[v] };
                return table;
            }
            return null;
        }

        static Dictionary<string, float>[] Parse(string[] rows, Func<string, string> rename)
        {
            var table = new Dictionary<string, float>[15];
            for (int v = 0; v < 15; v++)
            {
                table[v] = new Dictionary<string, float>();
                var parts = rows[v].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i + 1 < parts.Length; i += 2)
                    table[v][rename(parts[i])] = float.Parse(parts[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
            return table;
        }
    }
}
