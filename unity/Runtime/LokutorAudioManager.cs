// Unity's microphone and speakers for the clients of the SDK: a whole conversation with VoiceAgentClient, or dictation
// with SpeechToTextClient, without a LokutorAgent.
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Lokutor
{
    [AddComponentMenu("Lokutor/Lokutor Audio Manager")]
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class LokutorAudioManager : MonoBehaviour, IAudioManager
    {
        [Tooltip("Empty: the default microphone.")]
        public string microphoneDevice = "";

        public PlaybackBuffer Playback { get; } = new PlaybackBuffer(LokutorApi.OutputRate);
        public LokutorMicrophone Mic { get; } = new LokutorMicrophone();
        public bool Muted { get => Mic.Muted; set => Mic.Muted = value; }

        LokutorVoicePlayer player;
        EventPump client;
        Action<float[], int, int> onAudio;

        /// <summary>Connects the client with this microphone and speaker, and raises its events on the main thread.</summary>
        public Task<bool> StartManaged(VoiceAgentClient voiceAgent)
        {
            client = voiceAgent;
            return voiceAgent.StartManagedAsync(this);
        }

        /// <summary>Connects the client with this microphone, and raises its events on the main thread.</summary>
        public Task<bool> StartManaged(SpeechToTextClient speechToText)
        {
            client = speechToText;
            return speechToText.ConnectAsync(this);
        }

        public bool StartMicrophone(Action<float[], int, int> handler)
        {
            onAudio = handler;
            return Mic.Start(microphoneDevice);
        }

        public void StopMicrophone() => Mic.Stop();

        public void PlayAudio(byte[] pcm16, int sampleRate)
        {
            if (Playback.SampleRate != sampleRate)
            {
                Playback.Clear();
                Playback.SampleRate = sampleRate;
            }
            Playback.Write(Pcm16.ToFloat(pcm16));
        }

        public void StopPlayback() => Playback.Clear();

        public float GetAmplitude() => Mic.Level;

        void Awake() => player = new LokutorVoicePlayer(GetComponent<AudioSource>(), Playback);

        void Update()
        {
            client?.Update();
            if (onAudio != null) Mic.Update(onAudio);
            player.Update();
        }

        void OnDisable()
        {
            if (client is VoiceAgentClient voiceAgent) voiceAgent.Disconnect();
            else if (client is SpeechToTextClient speechToText) speechToText.Disconnect();
            client?.Update();
            Mic.Stop();
            Playback.Clear();
            player?.Stop();
        }
    }
}
