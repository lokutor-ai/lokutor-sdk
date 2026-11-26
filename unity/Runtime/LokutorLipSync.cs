// Moves a mouth with a voice: blend shapes, a jaw, or 2D drawings. LokutorAgent and LokutorVoice feed it; or feed it
// yourself with PushVisemes and Advance.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Lokutor
{
    /// <summary>One blend shape of one viseme, for rigs whose names are not recognised.</summary>
    [Serializable]
    public class LokutorVisemeShape
    {
        [Tooltip("sil PP FF TH DD kk CH SS nn RR aa E ih oh ou")]
        public string viseme = "aa";
        public string blendShape = "";
        [Range(0f, 1f)] public float weight = 1f;

        public LokutorVisemeShape() { }

        public LokutorVisemeShape(string viseme, string blendShape, float weight = 1f)
        {
            this.viseme = viseme;
            this.blendShape = blendShape;
            this.weight = weight;
        }
    }

    /// <summary>The drawing of one viseme.</summary>
    [Serializable]
    public class LokutorVisemeSprite
    {
        [Tooltip("sil PP FF TH DD kk CH SS nn RR aa E ih oh ou")]
        public string viseme = "aa";
        public Sprite sprite;
    }

    [AddComponentMenu("Lokutor/Lokutor Lip Sync")]
    public class LokutorLipSync : MonoBehaviour
    {
        [Header("Blend shapes")]
        [Tooltip("The meshes whose mouth moves: usually the face, plus teeth or tongue if they have the shapes too. Empty: every mesh under this object whose mouth is recognised.")]
        public List<SkinnedMeshRenderer> faces = new List<SkinnedMeshRenderer>();

        [Tooltip("Your rig, when its names are not recognised (Oculus, Ready Player Me and VRChat visemes, VRM and VRoid, ARKit, or a mouth-open shape).")]
        public List<LokutorVisemeShape> mapping = new List<LokutorVisemeShape>();

        [Header("Jaw")]
        [Tooltip("A jaw bone (or head part), rotated around its local X axis.")]
        public Transform jaw;
        public float jawMaxAngle = 12f;

        [Header("2D")]
        [Tooltip("A sprite whose drawing changes with the mouth.")]
        public SpriteRenderer mouthSprite;
        [Tooltip("0 closed, 1 slightly open, 2 open, 3 round (if there are that many).")]
        public Sprite[] mouthSprites = new Sprite[0];
        [Tooltip("Or a drawing for each viseme; the ones missing use sil's.")]
        public List<LokutorVisemeSprite> visemeSprites = new List<LokutorVisemeSprite>();

        [Header("Feel")]
        [Tooltip("How far the mouth opens.")]
        [Range(0f, 2f)] public float intensity = 1f;
        [Tooltip("Seconds the mouth leads the sound: people open before they speak.")]
        [Range(0f, 0.2f)] public float lead = 0.05f;
        [Tooltip("Seconds over which one shape melts into the next. 0.05 is crisp and cartoonish, 0.12 soft.")]
        [Range(0.01f, 0.3f)] public float blend = 0.07f;
        [Tooltip("How much quiet speech closes the mouth.")]
        [Range(0f, 1f)] public float loudness = 0.35f;
        [Tooltip("Steps per second of the mouth, for a hand-drawn look (12 = animation on twos). 0 = smooth.")]
        public float steppedFps;
        [Tooltip("Loudness multiplier for mouths that follow the loudness (no visemes).")]
        [Range(0.2f, 5f)] public float gain = 1.5f;

        [Header("Events")]
        [Tooltip("The mouth changed shape: its name.")]
        public LokutorTextEvent onVisemeChanged = new LokutorTextEvent();

        /// <summary>The mouth's timeline, without the engine.</summary>
        public LipSync Core { get; } = new LipSync();
        /// <summary>Weight of each of the 15 visemes right now (LokutorApi.Visemes order).</summary>
        public float[] Weights => Core.Weights;
        /// <summary>The strongest one.</summary>
        public string Current => Core.Current;
        /// <summary>How open the mouth is, 0..1.</summary>
        public float MouthOpen => Core.MouthOpen;
        public bool HasVisemes => Core.HasVisemes;
        /// <summary>Which kind of mouth was found.</summary>
        public MouthRigKind Rig
        {
            get
            {
                if (!bound) Bind();
                return rig;
            }
        }

        struct Target
        {
            public SkinnedMeshRenderer Mesh;
            public MouthRig.Shape Shape;
        }

        readonly List<Target> targets = new List<Target>();
        MouthRigKind rig = MouthRigKind.None;
        Transform boundJaw;
        Quaternion jawRest;
        Sprite shown;
        bool bound;

        void Awake()
        {
            Core.VisemeChanged += v => onVisemeChanged.Invoke(v);
            BindJaw();
        }

        /// <summary>Finds the mouth. Done by itself the first time; call it after changing faces or mapping.</summary>
        public void Bind()
        {
            bound = true;
            targets.Clear();
            rig = MouthRigKind.None;
            var names = Mapping();
            if (faces.Count > 0)
                foreach (var f in faces) BindFace(f, names);
            else
                foreach (var f in GetComponentsInChildren<SkinnedMeshRenderer>(true)) BindFace(f, names);
        }

        Dictionary<string, Dictionary<string, float>> Mapping()
        {
            var all = new Dictionary<string, Dictionary<string, float>>();
            foreach (var m in mapping)
            {
                if (m == null || string.IsNullOrEmpty(m.blendShape) || string.IsNullOrEmpty(m.viseme)) continue;
                if (!all.TryGetValue(m.viseme, out var row)) all[m.viseme] = row = new Dictionary<string, float>();
                row[m.blendShape] = m.weight;
            }
            return all;
        }

        void BindFace(SkinnedMeshRenderer face, Dictionary<string, Dictionary<string, float>> names)
        {
            if (face == null || face.sharedMesh == null) return;
            var shapes = new List<string>();
            for (int i = 0; i < face.sharedMesh.blendShapeCount; i++) shapes.Add(face.sharedMesh.GetBlendShapeName(i));
            var found = MouthRig.Find(shapes, names);
            if (found.Shapes.Count == 0) return;
            if (rig == MouthRigKind.None) rig = found.Kind;
            foreach (var s in found.Shapes) targets.Add(new Target { Mesh = face, Shape = s });
        }

        void BindJaw()
        {
            if (jaw == boundJaw) return;
            boundJaw = jaw;
            if (jaw != null) jawRest = jaw.localRotation;
        }

        /// <summary>The visemes of the reply being heard, as they arrive.</summary>
        public void PushVisemes(IEnumerable<Viseme> list) => Core.PushVisemes(list);

        /// <summary>A new reply, or silence: the mouth closes.</summary>
        public void Clear() => Core.Clear();

        /// <summary>
        /// One frame of the mouth. Heard is how much of the reply has been heard, in seconds; level its loudness (RMS);
        /// speaking whether it is playing at all.
        /// </summary>
        public void Advance(float heard, float level, bool speaking, float delta)
        {
            Core.Intensity = intensity;
            Core.Lead = lead;
            Core.Blend = blend;
            Core.Loudness = loudness;
            Core.SteppedFps = steppedFps;
            Core.Gain = gain;
            Core.Advance(heard, level, speaking, delta);
        }

        // A character with an agent and written lines has one mouth: whoever speaks has it, until the other one does.
        object speaker;
        bool speakerBusy;

        internal void Feed(object who, float heard, float level, bool speaking, float delta)
        {
            if (speaker == null || (speaking && !speakerBusy)) speaker = who;
            if (speaker != who) return;
            speakerBusy = speaking;
            Advance(heard, level, speaking, delta);
        }

        internal void Release(object who)
        {
            if (speaker != who) return;
            speaker = null;
            speakerBusy = false;
            Clear();
            Advance(0f, 0f, false, 1f);
        }

        // After the Animator, which may write the same bones and blend shapes.
        void LateUpdate() => Apply();

        /// <summary>Puts the mouth's shape on the face. Done by itself every frame.</summary>
        public void Apply()
        {
            if (!bound) Bind();
            foreach (var t in targets)
                if (t.Mesh != null) t.Mesh.SetBlendShapeWeight(t.Shape.Index, t.Shape.Weight(Core.Weights, intensity) * 100f);
            BindJaw();
            if (jaw != null) jaw.localRotation = jawRest * Quaternion.Euler(Core.MouthOpen * jawMaxAngle, 0f, 0f);
            if (mouthSprite == null) return;
            var sprite = MouthSprite();
            if (sprite == null || sprite == shown) return;
            mouthSprite.sprite = shown = sprite;
        }

        /// <summary>The drawing of the mouth's shape, for a UI Image or a material of yours. Null when there are none.</summary>
        public Sprite MouthSprite()
        {
            if (visemeSprites.Count == 0)
                return mouthSprites.Length > 0 ? mouthSprites[Core.SpriteFrame(mouthSprites.Length)] : null;
            Sprite rest = null;
            foreach (var v in visemeSprites)
            {
                if (v.viseme == Core.Current && v.sprite != null) return v.sprite;
                if (v.viseme == "sil") rest = v.sprite;
            }
            return rest;
        }
    }
}
