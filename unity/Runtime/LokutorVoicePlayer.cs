// Plays a voice through an AudioSource as it arrives, and tells its buffer how much of it has been heard. LokutorAgent,
// LokutorVoice and LokutorAudioManager have one each.
using System;
using UnityEngine;

namespace Lokutor
{
    public sealed class LokutorVoicePlayer : IDisposable
    {
        // A looping clip that is written a little ahead of the play head.
        const float LeadSeconds = 0.08f;

        /// <summary>The voice just given to the AudioSource, as it is: for a character that another character listens to. The array is reused.</summary>
        public event Action<float[], int> VoicePlayed;

        public AudioSource Source { get; }
        public PlaybackBuffer Buffer { get; }
        /// <summary>When the voice written so far will have come out of the speakers (the mixer's delay included).</summary>
        public DateTime AudibleUntilUtc { get; private set; }

        AudioClip clip;
        int rate, length, lead, block, writePos, erasePos, lastPlay, seenClears;
        long playTotal, writeTotal;      // samples of the clip's endless timeline: played, written up to
        long skew;                       // of that timeline over the buffer's count of samples read
        float[] blockBuf, silence;
        float sinceMix;

        public LokutorVoicePlayer(AudioSource source, PlaybackBuffer buffer)
        {
            Source = source;
            Buffer = buffer;
            source.playOnAwake = false;
            source.loop = true;
            source.dopplerLevel = 0f;
        }

        /// <summary>Call once per frame.</summary>
        public void Update()
        {
            if (AudioListener.pause && !Source.ignoreListenerPause) return;
            if (clip == null || rate != Buffer.SampleRate) Open();
            if (!Source.isPlaying)
            {
                Source.Play();
                Resync();
            }

            int play = Source.timeSamples;
            int step = (play - lastPlay + length) % length;
            if (step > length - lead) step = 0;          // the play head jittered back
            else lastPlay = play;
            playTotal += step;
            sinceMix = step > 0 ? 0f : sinceMix + Time.unscaledDeltaTime;
            if (playTotal > writeTotal)
            {
                // A frame so long that the play head overtook the voice: go on just ahead of it.
                Buffer.MarkPlayed(Buffer.TotalRead);
                _ = Buffer.HeardSeconds;
                Resync();
            }
            CutIfCleared();

            bool voiced = false;
            while (writeTotal - playTotal < 2 * lead)
            {
                bool voice = Buffer.Read(blockBuf, 0, block) > 0;
                CutIfCleared();
                clip.SetData(blockBuf, writePos);
                writePos = (writePos + block) % length;
                writeTotal += block;
                if (!voice) continue;
                voiced = true;
                VoicePlayed?.Invoke(blockBuf, rate);
            }
            // What has been played becomes silence, so a long frame is heard as a pause and not as an echo.
            while ((lastPlay - erasePos + length) % length >= 2 * block)
            {
                clip.SetData(silence, erasePos);
                erasePos = (erasePos + block) % length;
            }

            // The play head moves a mixer block at a time, and what it passes is heard after the mixer's own buffers.
            AudioSettings.GetDSPBufferSize(out int dspLength, out int dspCount);
            float dspBlock = (float)dspLength / Math.Max(1, AudioSettings.outputSampleRate), delay = dspBlock * dspCount;
            if (voiced) AudibleUntilUtc = DateTime.UtcNow.AddSeconds((double)(writeTotal - playTotal) / rate + delay);
            Buffer.MarkPlayed(playTotal - skew + (long)((Math.Min(sinceMix, dspBlock) - delay) * rate));
        }

        // The buffer was cleared (an interruption, a new reply): what was read before is not to be heard.
        void CutIfCleared()
        {
            int clears = Buffer.Clears;
            if (clears == seenClears) return;
            seenClears = clears;
            Resync();
            AudibleUntilUtc = DateTime.MinValue;
        }

        void Open()
        {
            Close();
            rate = Buffer.SampleRate;
            // 1 s in 10 ms blocks, so a block never straddles the loop point.
            block = Math.Max(1, rate / 100);
            length = block * 100;
            lead = (int)(rate * LeadSeconds);
            blockBuf = new float[block];
            silence = new float[block];
            clip = AudioClip.Create("Lokutor voice", length, 1, rate, false);
            Source.clip = clip;
            Source.loop = true;
            Source.Play();
            Resync();
        }

        // Nothing is waiting in the clip, and writing goes on just ahead of the play head.
        void Resync()
        {
            lastPlay = Source.timeSamples;
            clip.SetData(new float[length], 0);
            erasePos = lastPlay / block * block;
            writePos = (lastPlay + lead) / block * block % length;
            writeTotal = playTotal + (writePos - lastPlay + length) % length;
            skew = writeTotal - Buffer.TotalRead;
        }

        void Close()
        {
            if (clip == null) return;
            Source.Stop();
            Source.clip = null;
            UnityEngine.Object.Destroy(clip);
            clip = null;
        }

        /// <summary>Silence, and lets go of the clip. Update() starts again.</summary>
        public void Stop()
        {
            if (Source != null) Close();
            AudibleUntilUtc = DateTime.MinValue;
        }

        public void Dispose() => Stop();
    }
}
