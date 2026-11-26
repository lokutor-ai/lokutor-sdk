// The microphone, in mono blocks as they arrive. LokutorAgent and LokutorAudioManager have one each.
using System;
using UnityEngine;

namespace Lokutor
{
    public sealed class LokutorMicrophone : IDisposable
    {
        /// <summary>Silence is handed over instead of sound: a conversation's microphone must not simply stop, its pauses are measured in it.</summary>
        public bool Muted;
        /// <summary>Loudness of the last block, 0..1.</summary>
        public float Level { get; private set; }
        public bool IsRunning => clip != null;
        public int Rate { get; private set; }

        AudioClip clip;
        string device;
        int position, block;
        float[] raw, mono;

        /// <summary>False (and a warning in the console) without a microphone, or when it cannot be opened.</summary>
        public bool Start(string deviceName = null)
        {
            if (clip != null) return true;
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[Lokutor] No microphone found; the character can talk but cannot hear.");
                return false;
            }
            device = string.IsNullOrEmpty(deviceName) ? null : deviceName;
            Microphone.GetDeviceCaps(device, out int minRate, out int maxRate);
            int rate = minRate == 0 && maxRate == 0 ? LokutorApi.InputRate : Mathf.Clamp(LokutorApi.InputRate, minRate, maxRate);
            clip = Microphone.Start(device, true, 1, rate);
            if (clip == null)
            {
                Debug.LogWarning("[Lokutor] The microphone could not be opened (permission?).");
                return false;
            }
            Rate = clip.frequency;
            position = 0;
            // 20 ms blocks that divide the 1 s clip exactly, so a block never straddles the wrap.
            block = Rate % 50 == 0 ? Rate / 50 : 0;
            raw = new float[block * clip.channels];
            mono = new float[block];
            return true;
        }

        public void Stop()
        {
            if (clip != null) Microphone.End(device);
            clip = null;
            Level = 0f;
        }

        public void Dispose() => Stop();

        /// <summary>Call once per frame: hands over what has arrived (samples, how many, rate). The array is reused.</summary>
        public void Update(Action<float[], int, int> onAudio)
        {
            if (clip == null) return;
            int now = Microphone.GetPosition(device);
            if (now < 0) return;
            int length = clip.samples;
            while (clip != null && block > 0 && (now - position + length) % length >= block)
            {
                Read(raw, mono, block);
                position = (position + block) % length;
                onAudio(mono, block, Rate);
            }
            // Odd sample rates: whatever has arrived, split at the wrap (allocates, but such devices are rare).
            while (clip != null && block == 0 && position != now)
            {
                int count = now > position ? now - position : length - position;
                var part = new float[count];
                Read(new float[count * clip.channels], part, count);
                position = (position + count) % length;
                onAudio(part, count, Rate);
            }
        }

        void Read(float[] from, float[] to, int count)
        {
            clip.GetData(from, position);
            int channels = clip.channels;
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += from[i * channels + c];
                to[i] = Muted ? 0f : sum / channels;
            }
            Level = Mathf.Clamp01(LokutorAudio.CalculateRMS(to, 0, count) * 4f);
        }
    }
}
