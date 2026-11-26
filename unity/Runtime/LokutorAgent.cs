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
        /// <summary>No microphone (e.g. you send audio yourself with FeedMicrophone).</summary>
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

        [Tooltip("The line above what SetContext tells the character. Write it in the language of the persona.")]
        public string contextHeading = "Right now in the game:";

        [Tooltip("F1-F5 (female) or M1-M5 (male), or a voice id from the Lokutor dashboard.")]
        public string voice = "F1";

        [Tooltip("en, es, fr, pt, ca, gl, eu... or auto: the character follows the language the player speaks.")]
        public string language = "en";

        [Tooltip("Shifts the voice up or down: small creatures, giants.")]
        [Range(-12f, 12f)]
        public float pitch;

        [Tooltip("Start talking as soon as the scene starts. Off: call StartConversation() (e.g. when the player walks up).")]
        public bool startOnAwake = true;

        [Tooltip("The voice comes from the character's position (3D). Off: heard the same everywhere (2D).")]
        public bool spatialVoice = true;

        [Tooltip("Open the connection again when it drops. The character does not remember what came before.")]
        public bool autoReconnect = true;

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

        [Tooltip("Mutes the microphone while the character talks, so speakers can't make it interrupt itself, and while any other Lokutor Agent or Lokutor Voice does, so that it does not answer them. Turn off when players use headphones and should be able to interrupt.")]
        public bool muteMicWhileSpeaking = true;

        [Tooltip("The server has the character say \"One moment\" or the like while an action runs. Off: it says nothing until its answer.")]
        public bool actionFiller;

        [Tooltip("What it begins to say before it calls an action is heard. Turn it off with an action that it must call before it answers (\"how funny that was\"): the model can write its plan there.")]
        public bool speakBeforeActions = true;

        [Header("Lip sync (optional)")]
        [Tooltip("Empty: the Lokutor Lip Sync on this object, under it or above it; without one, a new one, which finds by itself the faces whose mouth it knows, or uses the fields below.")]
        public LokutorLipSync lipSync;

        [Tooltip("A face mesh, when it is not under this object.")]
        public SkinnedMeshRenderer face;

        [Tooltip("Its mouth-open blend shape, when its names are not recognised.")]
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
        [Tooltip("The player cut the character off: what they heard.")]
        public LokutorTextEvent onCharacterInterrupted = new LokutorTextEvent();
        public LokutorStateEvent onStateChanged = new LokutorStateEvent();
        [Tooltip("The conversation is over: why.")]
        public LokutorTextEvent onConversationEnded = new LokutorTextEvent();
        [Tooltip("The connection dropped and is being opened again.")]
        public UnityEvent onReconnecting = new UnityEvent();
        [Tooltip("It is open again, in a fresh conversation.")]
        public UnityEvent onReconnected = new UnityEvent();

        /// <summary>The player's words while they are still talking.</summary>
        public event Action<string> PlayerSaidPartial;
        /// <summary>An action ran: name, arguments, result.</summary>
        public event Action<string, ActionArgs, string> ActionCalled;
        /// <summary>Mouth shapes of the reply, as they arrive.</summary>
        public event Action<IReadOnlyList<Viseme>> VisemesReceived;
        /// <summary>The voice as it is given to the AudioSource (samples, rate): for a character that another character listens to. The array is reused.</summary>
        public event Action<float[], int> VoicePlayed;
        /// <summary>The server announced how many seconds this session may still run.</summary>
        public event Action<int> SessionLimit;
        public event Action<LokutorError> Error;

        /// <summary>
        /// For a shipped game: returns a short-lived ticket from YOUR server, which calls
        /// POST https://api.lokutor.com/ws-ticket with the API key. Set before StartConversation().
        /// </summary>
        public Func<Task<string>> TicketProvider;
        /// <summary>Your own OpenAI-compatible language model. Set before StartConversation().</summary>
        public CustomLLMConfig Llm;
        /// <summary>Your own language model, running in the game: return the reply, or write it with reply.Chunk. Set before StartConversation().</summary>
        public Func<LlmRequest, LlmReply, Task<string>> OnLlmRequest;
        [NonSerialized] public string url = LokutorApi.VoiceAgentUrl;

        /// <summary>The underlying session (events, raw audio). Null until StartConversation().</summary>
        public LokutorSession Session => session;
        public LokutorVoicePlayer Player { get; private set; }
        public LokutorMicrophone Mic { get; } = new LokutorMicrophone();
        /// <summary>The player has muted their microphone.</summary>
        public bool Muted { get; set; }
        public AgentState State => session != null ? session.State : AgentState.Disconnected;
        /// <summary>How open the mouth should be right now, 0..1 (already follows the audio you hear).</summary>
        public float MouthOpen => lipSync != null ? lipSync.MouthOpen : 0f;
        /// <summary>How much of the current reply has been heard, in seconds: the clock viseme times are read against.</summary>
        public float HeardSeconds => session != null ? session.HeardSeconds : 0f;
        /// <summary>Loudness of the character's voice right now (RMS).</summary>
        public float VoiceLevel => session != null ? session.Playback.HeardLevel : 0f;
        /// <summary>The character's voice is being heard.</summary>
        public bool IsSpeaking => session != null && session.IsSpeaking;

        internal static string UserAgent => "lokutor-unity/" + LokutorApi.Version + " (" + Application.unityVersion + ")";

        readonly List<AgentAction> codeActions = new List<AgentAction>();
        readonly Dictionary<string, string> context = new Dictionary<string, string>();
        List<TranscriptLine> lastTranscript = new List<TranscriptLine>();
        LokutorSession session;
        bool talkingOverride;

        void Reset()
        {
            // Editor only: sensible distances for a talking character when the component is added.
            var s = GetComponent<AudioSource>();
            s.minDistance = 1.5f;
            s.maxDistance = 25f;
        }

        void Awake()
        {
            // Also here, not only in Reset(): Reset never runs for a component added from code.
            var source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = spatialVoice ? 1f : 0f;
        }

        void Start()
        {
            if (startOnAwake) StartConversation();
        }

        // ---- Actions -----------------------------------------------------------------------------

        /// <summary>Lets the character call a game function. Works before or during a conversation.</summary>
        public LokutorAgent AddAction(string name, string description, Func<ActionArgs, string> handler, params ActionParam[] parameters)
            => AddAction(new AgentAction(name, description, handler, parameters));

        /// <summary>An action that takes a while (a walk, a web request): the character waits for the result.</summary>
        public LokutorAgent AddActionAsync(string name, string description, Func<ActionArgs, Task<string>> handler, params ActionParam[] parameters)
            => AddAction(AgentAction.Async(name, description, handler, parameters));

        public LokutorAgent AddAction(AgentAction action)
        {
            codeActions.RemoveAll(x => x.Name == action.Name);
            codeActions.Add(action);
            session?.AddAction(action);
            return this;
        }

        /// <summary>The character can no longer call it.</summary>
        public void RemoveAction(string name)
        {
            codeActions.RemoveAll(x => x.Name == name);
            session?.RemoveAction(name);
        }

        // ---- Conversation ------------------------------------------------------------------------

        /// <summary>Opens the conversation: the character greets the player and starts listening.</summary>
        public async void StartConversation()
        {
            if (State != AgentState.Disconnected) return;
            string key = !string.IsNullOrEmpty(apiKey) ? apiKey
                       : !string.IsNullOrEmpty(DefaultApiKey) ? DefaultApiKey
                       : Environment.GetEnvironmentVariable("LOKUTOR_API_KEY");
            if (string.IsNullOrEmpty(key) && TicketProvider == null)
            {
                Debug.LogError("[Lokutor] No API key. Paste one into the Lokutor Agent component (Account > Api Key), " +
                               "set LokutorAgent.DefaultApiKey, or set TicketProvider for a shipped game.", this);
                return;
            }
            Player?.Stop();
            PrepareLipSync();
            var s = NewSession(key);
            session = s;
            Player = new LokutorVoicePlayer(GetComponent<AudioSource>(), s.Playback);
            Player.VoicePlayed += (samples, rate) => VoicePlayed?.Invoke(samples, rate);
            try
            {
                await s.ConnectAsync();
            }
            catch (Exception e)
            {
                if (session != s) return;          // stopped while connecting
                s.Update();
                session = null;
                Player.Stop();
                Debug.LogError("[Lokutor] " + e.Message, this);
                Error?.Invoke(e.InnerException is LokutorException l ? l.Error : new LokutorError("ws.close", e.Message));
                onConversationEnded.Invoke(e.Message);
                return;
            }
            if (session == s && micMode != MicMode.Off) Mic.Start(microphoneDevice);
        }

        LokutorSession NewSession(string key)
        {
            var s = new LokutorSession(new SessionOptions
            {
                ApiKey = key,
                TicketProvider = TicketProvider,
                AgentId = string.IsNullOrEmpty(agentId) ? null : agentId,
                Persona = string.IsNullOrEmpty(persona) ? null : persona,
                ContextHeading = contextHeading,
                Voice = string.IsNullOrEmpty(voice) ? null : voice,
                Language = string.IsNullOrEmpty(language) ? null : language,
                Pitch = pitch,
                Llm = Llm,
                OnLlmRequest = OnLlmRequest,
                AutoReconnect = autoReconnect,
                MuteMicWhileSpeaking = muteMicWhileSpeaking,
                // Phones and browsers hear their own speaker later (more buffering on both sides of the sound card).
                EchoTailMs = Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer ? 700 : 300,
                ActionFiller = actionFiller,
                SpeakBeforeActions = speakBeforeActions,
                Url = url,
                UserAgent = UserAgent,
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
            s.OthersAudible = OthersAudible;
            foreach (var c in context) s.SetContext(c.Key, c.Value);
            s.PlayerSaid += t => onPlayerSaid.Invoke(t);
            s.PlayerSaidPartial += t => PlayerSaidPartial?.Invoke(t);
            s.AgentSaid += t => onCharacterSaid.Invoke(t);
            s.AgentInterrupted += t => onCharacterInterrupted.Invoke(t);
            s.StateChanged += st => onStateChanged.Invoke(st);
            s.ActionCalled += (name, args, result) => ActionCalled?.Invoke(name, args, result);
            s.VoiceCleared += () => { if (lipSync != null) lipSync.Clear(); };
            s.Visemes += list =>
            {
                if (lipSync != null) lipSync.PushVisemes(list);
                VisemesReceived?.Invoke(list);
            };
            s.SessionLimit += seconds => SessionLimit?.Invoke(seconds);
            s.Reconnecting += () => onReconnecting.Invoke();
            s.Reconnected += () => onReconnected.Invoke();
            s.Error += e =>
            {
                Debug.LogWarning("[Lokutor] " + e, this);
                Error?.Invoke(e);
            };
            s.Disconnected += reason =>
            {
                Mic.Stop();
                Player.Stop();
                lastTranscript = s.GetTranscript();
                onConversationEnded.Invoke(reason);
            };
            return s;
        }

        /// <summary>Ends the conversation and releases the microphone.</summary>
        public void StopConversation()
        {
            Mic.Stop();
            var s = session;
            if (s != null)
            {
                _ = s.DisconnectAsync();
                s.Update();            // what that raises
                session = null;
            }
            Player?.Stop();
            if (lipSync != null) lipSync.Release(this);
        }

        /// <summary>Changes who the character is, mid-conversation.</summary>
        public void SetPersona(string newPersona)
        {
            persona = newPersona;
            session?.SetPersona(newPersona);
        }

        /// <summary>
        /// Tells the character something about the game right now ("Time left", "20 seconds"), without rewriting its
        /// persona. Null takes it out. A dashboard agent needs a persona here for this.
        /// </summary>
        public void SetContext(string key, string value)
        {
            if (value == null) context.Remove(key);
            else context[key] = value;
            session?.SetContext(key, value);
        }

        public void SetVoice(string newVoice)
        {
            voice = newVoice;
            session?.SetVoice(newVoice);
        }

        /// <summary>"auto": the character follows the language the player speaks.</summary>
        public void SetLanguage(string newLanguage)
        {
            language = newLanguage;
            session?.SetLanguage(newLanguage);
        }

        public void SetPitch(float newPitch)
        {
            pitch = newPitch;
            session?.SetPitch(newPitch);
        }

        /// <summary>Forgets the conversation so far (the character starts fresh).</summary>
        public void ResetConversation() => session?.ResetConversation();

        /// <summary>The conversation so far, or the last one.</summary>
        public List<TranscriptLine> GetTranscript() => session != null ? session.GetTranscript() : lastTranscript;

        /// <summary>Push-to-talk from any input: true while the player holds the button.</summary>
        public void SetTalking(bool talking) => talkingOverride = talking;

        /// <summary>What the character hears, when it does not come from the microphone (MicMode.Off): mono, any rate. Keep sending silence too.</summary>
        public void FeedMicrophone(float[] samples, int offset, int count, int sampleRate) => session?.SendMicrophone(samples, offset, count, sampleRate);

        void Update()
        {
            var s = session;
            if (s == null) return;
            s.Update();
            if (session != s) return;          // one of the events stopped it
            s.MuteMicWhileSpeaking = muteMicWhileSpeaking;
            s.ActionFiller = actionFiller;
            s.SpeakBeforeActions = speakBeforeActions;
            Mic.Muted = Muted || !MicOpen();
            Mic.Update(Hear);
            if (s.State != AgentState.Disconnected)
            {
                Player.Update();
                s.SetVoiceAudibleUntil(Player.AudibleUntilUtc);
            }
            if (lipSync != null) lipSync.Feed(this, s.HeardSeconds, s.Playback.HeardLevel, s.IsSpeaking, Time.deltaTime);
        }

        void Hear(float[] samples, int count, int rate) => session?.SendMicrophone(samples, 0, count, rate);

        static readonly List<LokutorAgent> agents = new List<LokutorAgent>();

        void OnEnable() => agents.Add(this);

        void OnDisable()
        {
            agents.Remove(this);
            StopConversation();
        }

        // Another character is being heard, so it could reach the microphone too.
        bool OthersAudible()
        {
            foreach (var a in agents)
                if (a != this && a.session != null && a.session.AgentAudible) return true;
            return LokutorVoice.AnyAudible;
        }

        // ---- Mouth and microphone ----------------------------------------------------------------

        internal static LokutorLipSync FindLipSync(Component on, LokutorLipSync chosen)
        {
            if (chosen != null) return chosen;
            var found = NearLipSync(on);
            return found != null ? found : on.gameObject.AddComponent<LokutorLipSync>();
        }

        static LokutorLipSync NearLipSync(Component on)
        {
            var under = on.GetComponentInChildren<LokutorLipSync>(true);
            return under != null ? under : on.GetComponentInParent<LokutorLipSync>();
        }

        void PrepareLipSync()
        {
            if (lipSync != null) return;
            bool made = NearLipSync(this) == null;
            lipSync = FindLipSync(this, null);
            if (!made) return;
            lipSync.gain = mouthGain;
            lipSync.jaw = jaw;
            lipSync.jawMaxAngle = jawMaxAngle;
            if (face == null) return;
            lipSync.faces.Add(face);
            if (string.IsNullOrEmpty(mouthBlendShape)) return;
            for (int v = 0; v < 15; v++)
                lipSync.mapping.Add(new LokutorVisemeShape(LokutorApi.Visemes[v], mouthBlendShape, LipSync.Openness[v]));
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
    }
}
