using System;

namespace Lokutor
{
    /// <summary>PCM16 little-endian helpers.</summary>
    public static class Pcm16
    {
        public static byte[] FromFloat(float[] samples, int offset, int count)
        {
            var bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                float f = samples[offset + i];
                if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                short v = (short)Math.Round(f * 32767f);
                bytes[i * 2] = (byte)(v & 0xff);
                bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
            }
            return bytes;
        }

        public static float[] ToFloat(byte[] bytes)
        {
            int n = bytes.Length / 2;
            var f = new float[n];
            for (int i = 0; i < n; i++)
                f[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8)) / 32768f;
            return f;
        }
    }

    /// <summary>
    /// Streaming mono resampler. Keeps its phase across calls, so audio can be pushed in chunks of
    /// any size (a game engine hands the microphone over in whatever blocks it has). Downsampling
    /// averages the input over each output step, which is enough anti-aliasing for speech going to
    /// a recogniser; upsampling interpolates linearly.
    /// </summary>
    public sealed class StreamingResampler
    {
        readonly double step; // input samples per output sample
        double pos;           // position of the next output sample, relative to the start of `carry`
        float[] carry = new float[0];

        public int InputRate { get; }
        public int OutputRate { get; }

        public StreamingResampler(int inputRate, int outputRate)
        {
            if (inputRate <= 0 || outputRate <= 0) throw new ArgumentException("rates must be positive");
            InputRate = inputRate;
            OutputRate = outputRate;
            step = (double)inputRate / outputRate;
        }

        public float[] Process(float[] input, int offset, int count)
        {
            if (InputRate == OutputRate)
            {
                var copy = new float[count];
                Array.Copy(input, offset, copy, 0, count);
                return copy;
            }
            // Join the leftover tail with the new block.
            var buf = new float[carry.Length + count];
            Array.Copy(carry, buf, carry.Length);
            Array.Copy(input, offset, buf, carry.Length, count);

            int maxOut = (int)((buf.Length - pos) / step) + 1;
            var output = new float[Math.Max(0, maxOut)];
            int n = 0;
            if (step > 1.0)
            {
                // Average every input sample that falls inside this output sample's window.
                while (pos + step <= buf.Length)
                {
                    int a = (int)pos, b = (int)(pos + step);
                    double sum = 0; int cnt = 0;
                    for (int k = a; k < b && k < buf.Length; k++) { sum += buf[k]; cnt++; }
                    output[n++] = cnt > 0 ? (float)(sum / cnt) : 0f;
                    pos += step;
                }
            }
            else
            {
                while (pos + 1 < buf.Length)
                {
                    int a = (int)pos;
                    double t = pos - a;
                    output[n++] = (float)(buf[a] * (1 - t) + buf[a + 1] * t);
                    pos += step;
                }
            }
            // Keep what the next call still needs, and rebase the position onto it.
            int keepFrom = Math.Min((int)pos, buf.Length);
            carry = new float[buf.Length - keepFrom];
            Array.Copy(buf, keepFrom, carry, 0, carry.Length);
            pos -= keepFrom;

            if (n == output.Length) return output;
            var trimmed = new float[n];
            Array.Copy(output, trimmed, n);
            return trimmed;
        }

        public void Reset()
        {
            carry = new float[0];
            pos = 0;
        }
    }

    /// <summary>
    /// The agent's voice, waiting to be played. Network threads write; the engine's audio callback
    /// reads at <see cref="SampleRate"/>. Silence is returned when it runs dry, so the engine can keep
    /// a looping source playing for the whole conversation. <see cref="Level"/> is the loudness of
    /// what was last read, 0..1, for amplitude lip-sync.
    /// </summary>
    public sealed class PlaybackBuffer
    {
        readonly object gate = new object();
        float[] ring;
        int head, count;
        volatile float level;
        long lastVoiceReadTicks;

        public int SampleRate { get; internal set; }

        public PlaybackBuffer(int sampleRate, int initialSeconds = 20)
        {
            SampleRate = sampleRate;
            ring = new float[Math.Max(1, sampleRate * initialSeconds)];
        }

        /// <summary>Seconds of audio waiting to be played.</summary>
        public float BufferedSeconds { get { lock (gate) return (float)count / SampleRate; } }

        public bool IsPlaying { get { lock (gate) return count > 0; } }

        /// <summary>Loudness of the most recently read block (RMS scaled to roughly 0..1).</summary>
        public float Level => level;

        /// <summary>When the engine last read some of the voice (UTC).</summary>
        public DateTime LastVoiceReadUtc => new DateTime(System.Threading.Interlocked.Read(ref lastVoiceReadTicks), DateTimeKind.Utc);

        public void Write(float[] samples)
        {
            lock (gate)
            {
                if (count + samples.Length > ring.Length) Grow(count + samples.Length);
                int tail = (head + count) % ring.Length;
                int first = Math.Min(samples.Length, ring.Length - tail);
                Array.Copy(samples, 0, ring, tail, first);
                if (first < samples.Length) Array.Copy(samples, first, ring, 0, samples.Length - first);
                count += samples.Length;
            }
        }

        /// <summary>Fills <paramref name="dst"/> (mono) with queued audio, then silence.</summary>
        public int Read(float[] dst, int offset, int n)
        {
            int got;
            lock (gate)
            {
                got = Math.Min(n, count);
                int first = Math.Min(got, ring.Length - head);
                Array.Copy(ring, head, dst, offset, first);
                if (first < got) Array.Copy(ring, 0, dst, offset + first, got - first);
                head = (head + got) % ring.Length;
                count -= got;
            }
            if (got < n) Array.Clear(dst, offset + got, n - got);
            if (got > 0) System.Threading.Interlocked.Exchange(ref lastVoiceReadTicks, DateTime.UtcNow.Ticks);
            double sum = 0;
            for (int i = 0; i < n; i++) { float v = dst[offset + i]; sum += v * v; }
            float rms = n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
            level = Math.Min(1f, rms * 4f);
            return got;
        }

        /// <summary>Drops everything queued (the caller interrupted, or a newer reply began).</summary>
        public void Clear()
        {
            lock (gate) { head = 0; count = 0; }
            level = 0f;
        }

        void Grow(int needed)
        {
            int size = ring.Length;
            while (size < needed) size *= 2;
            var next = new float[size];
            int first = Math.Min(count, ring.Length - head);
            Array.Copy(ring, head, next, 0, first);
            if (first < count) Array.Copy(ring, 0, next, first, count - first);
            ring = next;
            head = 0;
        }
    }
}
