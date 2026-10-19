// Drop this on any character to make it a voice agent: it listens through the microphone, answers in
// its own voice from its own position in the world (3D audio), moves its mouth, and can call your
// game's functions ("actions") when the conversation calls for it.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Lokutor
{
    public enum MicMode
    {
        /// <summary>The microphone is always open; the server detects when the player is talking.</summary>
        Always,
        /// <summary>Only while the push-to-talk key (or SetTalking(true)) is held.</summary>
        PushToTalk,
        /// <summary>No microphone (e.g. you send audio yourself through Session.SendMicrophone).</summary>
        Off,
    }

    /// <summary>A game action set up in the Inspector: the character calls it, onCalled runs.</summary>
    [Serializable]
    public class LokutorActionBinding
    {
        [Tooltip("What the character calls it, e.g. open_gate. Letters, digits and _ only.")]
        public string name = "open_gate";

        [Tooltip("When the character should use it, in plain words.")]
        [TextArea(2, 4)]
        public string description = "Opens the town gate for the player.";

        [Tooltip("What the character is told happened, so it can say so.")]
        public string reply = "The gate is now open.";

        public UnityEvent onCalled = new UnityEvent();
    }

    [Serializable] public class LokutorTextEvent : UnityEvent<string> { }
    [Serializable] public class LokutorStateEvent : UnityEvent<AgentState> { }

    [AddComponentMenu("Lokutor/Lokutor Agent")]
    [RequireComponent(typeof(AudioSource))]
    public class LokutorAgent : MonoBehaviour
    {
        /// <summary>Used by every agent whose apiKey field is empty. Set it once from code if you prefer.</summary>
        public static string DefaultApiKey;

        [Header("Account")]
        [Tooltip("Your Lokutor API key (dashboard > API keys). Fine while prototyping; a shipped game should use TicketProvider so the key stays on your server. Empty: LokutorAgent.DefaultApiKey, then the LOKUTOR_API_KEY environment variable.")]
        public string apiKey = "";

        [Tooltip("Optional: the id of an agent built in the Lokutor dashboard. Its persona, voice and language are used unless set below.")]
        public string agentId = "";

        [Header("Character")]
        [Tooltip("Who the character is, what it knows and how it talks.")]
        [TextArea(4, 12)]
        public string persona = "You are a friendly villager in a fantasy town. Answer in one or two short sentences.";

        [Tooltip("F1-F5 (female) or M1-M5 (male), or a voice id from the Lokutor dashboard.")]
        public string voice = "F1";

        [Tooltip("en, es, fr, pt, ca, gl, eu...")]
        public string language = "en";

        [Tooltip("Start talking as soon as the scene starts. Off: call StartConversation() (e.g. when the player walks up).")]
        public bool startOnAwake = true;

        [Tooltip("The voice comes from the character's position (3D). Off: heard the same everywhere (2D).")]
        public bool spatialVoice = true;

        [Header("Microphone")]
        public MicMode micMode = MicMode.Always;

        [Tooltip("Held to talk in PushToTalk mode (old Input Manager). With the new Input System use pushToTalkAction or call SetTalking().")]
        public KeyCode pushToTalkKey = KeyCode.T;

#if LOKUTOR_INPUT_SYSTEM
        [Tooltip("Held to talk in PushToTalk mode (Input System).")]
        public UnityEngine.InputSystem.InputActionReference pushToTalkAction;
#endif

        [Tooltip("Empty: the default microphone.")]
        public string microphoneDevice = "";

        [Tooltip("Mutes the microphone while the character talks, so speakers can't make it interrupt itself. Turn off when players use headphones and should be able to interrupt.")]
        public bool muteMicWhileSpeaking = true;

        [Header("Lip sync (optional)")]
        [Tooltip("A face mesh with a mouth-open blend shape.")]
        public SkinnedMeshRenderer face;

        [Tooltip("The blend shape's name, e.g. mouthOpen, jawOpen, MTH_A.")]
        public string mouthBlendShape = "";

        [Tooltip("Or a jaw bone, rotated around its local X axis.")]
        public Transform jaw;

        public float jawMaxAngle = 12f;

        [Range(0.2f, 5f)]
        public float mouthGain = 1.5f;

        [Header("Game actions the character can use")]
        public List<LokutorActionBinding> actions = new List<LokutorActionBinding>();

        [Header("Events")]
        public LokutorTextEvent onPlayerSaid = new LokutorTextEvent();
        public LokutorTextEvent onCharacterSaid = new LokutorTextEvent();
        public LokutorStateEvent onStateChanged = new LokutorStateEvent();

        /// <summary>
        /// For a shipped game: returns a short-lived ticket from YOUR server, which calls
        /// POST https://api.lokutor.com/ws-ticket with the API key. Set before StartConversation().
        /// </summary>
        public Func<Task<string>> TicketProvider;

        /// <summary>The underlying session (events, transcripts, raw audio). Null until StartConversation().</summary>
        public LokutorSession Session => session;
        public AgentState State => session != null ? session.State : AgentState.Disconnected;
        /// <summary>How open the mouth should be right now, 0..1 (already follows the audio you hear).</summary>
        public float MouthOpen { get; private set; }

        readonly List<AgentAction> codeActions = new List<AgentAction>();
        LokutorSession session;
        AudioSource source;
        bool talkingOverride;
        bool starting;

        // Playback: a looping clip that is written a little ahead of the play head.
        AudioClip voiceClip;
        float[] written;      // a copy of the clip's samples, for lip sync at the play head
        int clipRate, writePos, block;
        float[] blockBuf;
        const float LeadSeconds = 0.08f;

        // Microphone
        AudioClip micClip;
        string micName;
        int micRate, micPos, micBlock;
        float[] micBlockBuf, micMono;

        int mouthIndex = -1;
        Quaternion jawRest;

        void Reset()
        {
            // Editor only: sensible distances for a talking character when the component is added.
            var s = GetComponent<AudioSource>();
            s.minDistance = 1.5f;
            s.maxDistance = 25f;
        }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            // Also here, not only in Reset(): Reset never runs for a component added from code.
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;
            source.spatialBlend = spatialVoice ? 1f : 0f;
            if (jaw != null) jawRest = jaw.localRotation;
        }

        void Start()
        {
            if (startOnAwake) StartConversation();
        }

        /// <summary>Lets the character call a game function. Works before or during a conversation.</summary>
        public LokutorAgent AddAction(string name, string description, Func<ActionArgs, string> handler, params ActionParam[] parameters)
        {
            var a = new AgentAction(name, description, handler, parameters);
            codeActions.RemoveAll(x => x.Name == a.Name);
            codeActions.Add(a);
            if (session != null)
            {
                session.AddAction(a);
                session.RefreshActions();
            }
            return this;
        }

        /// <summary>Opens the conversation: the character greets the player and starts listening.</summary>
        public async void StartConversation()
        {
            if (session != null || starting) return;
            string key = !string.IsNullOrEmpty(apiKey) ? apiKey
                       : !string.IsNullOrEmpty(DefaultApiKey) ? DefaultApiKey
                       : Environment.GetEnvironmentVariable("LOKUTOR_API_KEY");
            if (string.IsNullOrEmpty(key) && TicketProvider == null)
            {
                Debug.LogError("[Lokutor] No API key. Paste one into the Lokutor Agent component (Account > Api Key), " +
                               "set LokutorAgent.DefaultApiKey, or set TicketProvider for a shipped game.", this);
                return;
            }
            starting = true;
            var s = new LokutorSession(new SessionOptions
            {
                ApiKey = key,
                TicketProvider = TicketProvider,
                AgentId = string.IsNullOrEmpty(agentId) ? null : agentId,
                Persona = string.IsNullOrEmpty(persona) ? null : persona,
                Voice = string.IsNullOrEmpty(voice) ? null : voice,
                Language = string.IsNullOrEmpty(language) ? null : language,
                MuteMicWhileSpeaking = muteMicWhileSpeaking,
                UserAgent = "lokutor-unity/0.1 (" + Application.unityVersion + ")",
            });
            foreach (var b in actions)
            {
                if (b == null || string.IsNullOrWhiteSpace(b.name)) continue;
                var binding = b;
                s.AddAction(binding.name.Trim(), binding.description, _ =>
                {
                    binding.onCalled.Invoke();
                    return string.IsNullOrEmpty(binding.reply) ? "Done." : binding.reply;
                });
            }
            foreach (var a in codeActions) s.AddAction(a);
            s.PlayerSaid += t => onPlayerSaid.Invoke(t);
            s.AgentSaid += t => onCharacterSaid.Invoke(t);
            s.StateChanged += st => onStateChanged.Invoke(st);
            s.Error += e => Debug.LogWarning("[Lokutor] " + e, this);
            s.Disconnected += reason => Debug.Log("[Lokutor] Conversation ended: " + reason, this);

            try
            {
                await s.ConnectAsync();
            }
            catch (Exception e)
            {
                starting = false;
                Debug.LogError("[Lokutor] " + e.Message, this);
                return;
            }
            starting = false;
            if (!this || !isActiveAndEnabled)
            {
                s.Dispose();
                return;
            }
            session = s;
            StartMicrophone();
        }

        /// <summary>Ends the conversation and releases the microphone.</summary>
        public void StopConversation()
        {
            StopMicrophone();
            var s = session;
            session = null;
            if (s != null) _ = s.DisconnectAsync();
            if (source != null) source.Stop();
            MouthOpen = 0f;
            ApplyMouth();
        }

        /// <summary>Changes who the character is, mid-conversation.</summary>
        public void SetPersona(string newPersona)
        {
            persona = newPersona;
            session?.SetPersona(newPersona);
        }

        /// <summary>Push-to-talk from any input: true while the player holds the button.</summary>
        public void SetTalking(bool talking) => talkingOverride = talking;

        void Update()
        {
            var s = session;
            if (s == null) return;
            s.Update();
            PumpMicrophone(s);
            PumpPlayback(s);
            UpdateMouth();
        }

        void LateUpdate() => ApplyMouth();

        void OnDisable() => StopConversation();

        // ---- Playback ----------------------------------------------------------------------------

        void PumpPlayback(LokutorSession s)
        {
            int rate = s.PlaybackRate;
            if (voiceClip == null || clipRate != rate)
            {
                if (voiceClip != null) { source.Stop(); Destroy(voiceClip); }
                clipRate = rate;
                voiceClip = AudioClip.Create("Lokutor voice", rate, 1, rate, false); // 1 s, looping
                written = new float[rate];
                voiceClip.SetData(written, 0);
                source.clip = voiceClip;
                source.loop = true;
                source.Play();
                // 10 ms blocks divide the 1 s clip exactly at every common rate, so a block never
                // straddles the loop point and needs no allocation.
                block = rate % 100 == 0 ? rate / 100 : 0;
                blockBuf = block > 0 ? new float[block] : null;
                writePos = Align((int)(rate * LeadSeconds));
            }
            if (!source.isPlaying) source.Play();

            int len = voiceClip.samples;
            int play = source.timeSamples;
            int lead = (int)(rate * LeadSeconds);
            int ahead = (writePos - play + len) % len;
            if (ahead > len / 2)
            {
                // The play head overtook us (a long frame): start writing just ahead of it again.
                writePos = Align((play + lead) % len);
                ahead = (writePos - play + len) % len;
            }
            if (block > 0)
            {
                int voicedAhead = -1;
                while (ahead < 2 * lead)
                {
                    if (s.Playback.Read(blockBuf, 0, block) > 0) voicedAhead = ahead + block;
                    voiceClip.SetData(blockBuf, writePos);
                    Array.Copy(blockBuf, 0, written, writePos, block);
                    writePos = (writePos + block) % len;
                    ahead += block;
                }
                if (voicedAhead > 0) ReportAudibleUntil(s, voicedAhead, rate);
                return;
            }
            // Unusual playback rates: one write per frame, split at the loop point.
            int need = 2 * lead - ahead;
            if (need <= 0) return;
            var chunk = new float[need];
            if (s.Playback.Read(chunk, 0, need) > 0) ReportAudibleUntil(s, ahead + need, rate);
            int first = Math.Min(need, len - writePos);
            WriteClip(writePos, chunk, 0, first);
            if (first < need) WriteClip(0, chunk, first, need - first);
            writePos = (writePos + need) % len;
        }

        int Align(int pos) => block > 0 ? pos / block * block : pos;

        // The voice just written plays after `samplesAhead` more samples and the mixer's own buffers:
        // until then (and the room's echo after) the microphone stays muted.
        void ReportAudibleUntil(LokutorSession s, int samplesAhead, int rate)
        {
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            double mixerSeconds = (double)bufferLength * numBuffers / Math.Max(1, AudioSettings.outputSampleRate);
            s.SetVoiceAudibleUntil(DateTime.UtcNow.AddSeconds((double)samplesAhead / rate + mixerSeconds));
        }

        void WriteClip(int offset, float[] src, int srcOffset, int count)
        {
            var part = new float[count];
            Array.Copy(src, srcOffset, part, 0, count);
            voiceClip.SetData(part, offset);
            Array.Copy(part, 0, written, offset, count);
        }

        void UpdateMouth()
        {
            if (voiceClip == null || written == null) { MouthOpen = 0f; return; }
            // Loudness of what is playing right now, so the mouth matches the sound, not the network.
            int len = written.Length, play = source.timeSamples, window = clipRate / 50;
            double sum = 0;
            for (int i = 0; i < window; i++) { float v = written[(play + i) % len]; sum += v * v; }
            float target = Mathf.Clamp01((float)Math.Sqrt(sum / window) * 4f * mouthGain);
            MouthOpen = Mathf.Lerp(MouthOpen, target, target > MouthOpen ? 0.6f : 0.25f);
        }

        void ApplyMouth()
        {
            if (face != null && !string.IsNullOrEmpty(mouthBlendShape))
            {
                if (mouthIndex < 0 && face.sharedMesh != null) mouthIndex = face.sharedMesh.GetBlendShapeIndex(mouthBlendShape);
                if (mouthIndex >= 0) face.SetBlendShapeWeight(mouthIndex, MouthOpen * 100f);
            }
            if (jaw != null) jaw.localRotation = jawRest * Quaternion.Euler(MouthOpen * jawMaxAngle, 0f, 0f);
        }

        // ---- Microphone --------------------------------------------------------------------------

        void StartMicrophone()
        {
            if (micMode == MicMode.Off) return;
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[Lokutor] No microphone found; the character can talk but cannot hear.", this);
                return;
            }
            micName = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
            Microphone.GetDeviceCaps(micName, out int minRate, out int maxRate);
            micRate = minRate == 0 && maxRate == 0 ? 16000 : Mathf.Clamp(16000, minRate, maxRate);
            micClip = Microphone.Start(micName, true, 1, micRate);
            if (micClip == null)
            {
                Debug.LogWarning("[Lokutor] The microphone could not be opened (permission?).", this);
                return;
            }
            micRate = micClip.frequency;
            micPos = 0;
            // Read in 20 ms blocks that divide the 1 s clip exactly, so a block never straddles the wrap.
            micBlock = micRate % 50 == 0 ? micRate / 50 : 0;
            micBlockBuf = micBlock > 0 ? new float[micBlock * micClip.channels] : null;
            micMono = new float[Math.Max(micBlock, 1)];
        }

        void StopMicrophone()
        {
            if (micClip != null) Microphone.End(micName);
            micClip = null;
        }

        bool MicOpen()
        {
            if (micMode == MicMode.Always) return true;
            if (micMode == MicMode.Off) return false;
            if (talkingOverride) return true;
#if LOKUTOR_INPUT_SYSTEM
            if (pushToTalkAction != null && pushToTalkAction.action != null)
            {
                if (!pushToTalkAction.action.enabled) pushToTalkAction.action.Enable();
                return pushToTalkAction.action.IsPressed();
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(pushToTalkKey);
#else
            return false;
#endif
        }

        void PumpMicrophone(LokutorSession s)
        {
            if (micClip == null) return;
            int pos = Microphone.GetPosition(micName);
            if (pos < 0) return;
            int len = micClip.samples, channels = micClip.channels;
            bool open = MicOpen();
            if (micBlock > 0)
            {
                while ((pos - micPos + len) % len >= micBlock)
                {
                    micClip.GetData(micBlockBuf, micPos);
                    for (int i = 0; i < micBlock; i++)
                    {
                        float sum = 0f;
                        for (int c = 0; c < channels; c++) sum += micBlockBuf[i * channels + c];
                        micMono[i] = open ? sum / channels : 0f;
                    }
                    s.SendMicrophone(micMono, 0, micBlock, micRate);
                    micPos = (micPos + micBlock) % len;
                }
                return;
            }
            // Odd sample rates: read whatever has arrived (allocates, but such devices are rare).
            int count = (pos - micPos + len) % len;
            if (count == 0) return;
            int first = Math.Min(count, len - micPos);
            var mono = new float[count];
            ReadMono(micPos, first, channels, mono, 0, open);
            if (first < count) ReadMono(0, count - first, channels, mono, first, open);
            s.SendMicrophone(mono, 0, count, micRate);
            micPos = pos;
        }

        void ReadMono(int offset, int count, int channels, float[] dst, int dstOffset, bool open)
        {
            var raw = new float[count * channels];
            micClip.GetData(raw, offset);
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += raw[i * channels + c];
                dst[dstOffset + i] = open ? sum / channels : 0f;
            }
        }
    }
}
