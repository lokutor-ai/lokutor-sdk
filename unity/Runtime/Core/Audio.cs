using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Lokutor
{
    /// <summary>Audio helpers, named as in the JavaScript and Python SDKs.</summary>
    public static class LokutorAudio
    {
        public static float[] Pcm16ToFloat32(byte[] pcm) => Pcm16.ToFloat(pcm);

        public static byte[] Float32ToPcm16(float[] samples) => Pcm16.FromFloat(samples, 0, samples.Length);

        public static float CalculateRMS(float[] samples) => CalculateRMS(samples, 0, samples.Length);

        public static float CalculateRMS(float[] samples, int offset, int count)
        {
            if (count <= 0) return 0f;
            double sum = 0;
            for (int i = 0; i < count; i++) { float v = samples[offset + i]; sum += v * v; }
            return (float)Math.Sqrt(sum / count);
        }

        public static float[] NormalizeAudio(float[] samples, float targetPeak = 0.95f)
        {
            float peak = 0f;
            foreach (var v in samples) peak = Math.Max(peak, Math.Abs(v));
            if (peak < 1e-5f) return samples;
            var louder = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++) louder[i] = samples[i] * targetPeak / peak;
            return louder;
        }

        public static float[] Resample(float[] samples, int fromRate, int toRate) => new StreamResampler(fromRate, toRate).Process(samples);

        /// <summary>A whole PCM16 mono recording as a WAV file's bytes.</summary>
        public static byte[] WavBytes(byte[] pcm, int rate)
        {
            using (var m = new MemoryStream(44 + pcm.Length))
            using (var w = new BinaryWriter(m))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                w.Flush();
                return m.ToArray();
            }
        }
    }

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
    public class StreamResampler
    {
        readonly double step; // input samples per output sample
        double pos;           // position of the next output sample, relative to the start of `carry`
        float[] carry = new float[0];

        public int InputRate { get; }
        public int OutputRate { get; }

        public StreamResampler(int inputRate, int outputRate)
        {
            if (inputRate <= 0 || outputRate <= 0) throw new ArgumentException("rates must be positive");
            InputRate = inputRate;
            OutputRate = outputRate;
            step = (double)inputRate / outputRate;
        }

        public float[] Process(float[] input) => Process(input, 0, input.Length);

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

    /// <summary>The name <see cref="StreamResampler"/> had before 0.2.</summary>
    public sealed class StreamingResampler : StreamResampler
    {
        public StreamingResampler(int inputRate, int outputRate) : base(inputRate, outputRate) { }
    }

    /// <summary>
    /// A voice, waiting to be played. Network threads write; the engine reads at <see cref="SampleRate"/>,
    /// a little ahead of what it is playing. Silence is returned when it runs dry, so the engine can keep
    /// a looping source going for the whole conversation.
    ///
    /// It also keeps the clock lip sync runs on: the engine says how much of what it read has come out
    /// (<see cref="MarkPlayed"/>), and <see cref="HeardSeconds"/> is how much of the current reply has
    /// been heard.
    /// </summary>
    public sealed class PlaybackBuffer
    {
        struct Span { public long Device, Voice; public int Count; }      // voice handed out at a device position
        struct Loudness { public long Voice; public float Rms; }

        readonly object gate = new object();
        readonly Queue<Span> spans = new Queue<Span>();
        readonly Queue<Loudness> loudness = new Queue<Loudness>();
        float[] ring;
        int head, count;
        volatile float level;
        long lastVoiceReadTicks;
        long totalRead, played = -1, voiceRead, heard;
        double blockSum;
        int blockCount, clears;

        public int SampleRate { get; internal set; }

        public PlaybackBuffer(int sampleRate, int initialSeconds = 20)
        {
            SampleRate = sampleRate;
            ring = new float[Math.Max(1, sampleRate * initialSeconds)];
        }

        /// <summary>Seconds of audio waiting to be read.</summary>
        public float BufferedSeconds { get { lock (gate) return (float)count / SampleRate; } }

        /// <summary>Audio is waiting to be read.</summary>
        public bool IsPlaying { get { lock (gate) return count > 0; } }

        /// <summary>The voice is not over: some waits to be read, or was read and has not come out yet.</summary>
        public bool IsAudible { get { lock (gate) return count > 0 || HeardSamples() < voiceRead; } }

        /// <summary>Loudness of the most recently read block (RMS scaled to roughly 0..1).</summary>
        public float Level => level;

        /// <summary>When the engine last read some of the voice (UTC).</summary>
        public DateTime LastVoiceReadUtc => new DateTime(Interlocked.Read(ref lastVoiceReadTicks), DateTimeKind.Utc);

        /// <summary>Samples read since the buffer was made, silence included.</summary>
        public long TotalRead { get { lock (gate) return totalRead; } }

        /// <summary>Grows with every Clear(), so an engine knows that what it read before one is not to be played.</summary>
        public int Clears { get { lock (gate) return clears; } }

        /// <summary>
        /// How many of the samples read so far (<see cref="TotalRead"/>) have come out of the speakers. An
        /// engine that never says is taken to play what it reads at once.
        /// </summary>
        public void MarkPlayed(long samples)
        {
            lock (gate) played = Math.Max(0, Math.Min(samples, totalRead));
        }

        /// <summary>How much of the current reply has been heard, in seconds: the clock viseme times are read against.</summary>
        public float HeardSeconds { get { lock (gate) return (float)HeardSamples() / SampleRate; } }

        /// <summary>Loudness (RMS) of what is being heard right now; 0 when nothing is.</summary>
        public float HeardLevel
        {
            get
            {
                lock (gate)
                {
                    long h = HeardSamples();
                    if (h >= voiceRead && count == 0) return 0f;
                    while (loudness.Count > 1 && loudness.Peek().Voice + SampleRate / 50 <= h) loudness.Dequeue();
                    return loudness.Count > 0 && loudness.Peek().Voice <= h ? loudness.Peek().Rms : 0f;
                }
            }
        }

        long HeardSamples()
        {
            long p = played < 0 ? totalRead : played;
            while (spans.Count > 0)
            {
                var s = spans.Peek();
                if (p < s.Device) break;
                heard = Math.Max(heard, s.Voice + Math.Min(s.Count, p - s.Device));   // it never runs backwards within a reply
                if (p < s.Device + s.Count) break;
                spans.Dequeue();
            }
            return heard;
        }

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
                if (got > 0)
                {
                    spans.Enqueue(new Span { Device = totalRead, Voice = voiceRead, Count = got });
                    Measure(dst, offset, got);
                    voiceRead += got;
                }
                totalRead += n;
                if (spans.Count > 4096) HeardSamples();       // nobody has asked for a while: let go of what was heard
            }
            if (got < n) Array.Clear(dst, offset + got, n - got);
            if (got > 0) Interlocked.Exchange(ref lastVoiceReadTicks, DateTime.UtcNow.Ticks);
            level = Math.Min(1f, LokutorAudio.CalculateRMS(dst, offset, n) * 4f);
            return got;
        }

        // Loudness of every 20 ms of voice.
        void Measure(float[] src, int offset, int n)
        {
            int block = Math.Max(1, SampleRate / 50);
            for (int i = 0; i < n; i++)
            {
                float v = src[offset + i];
                blockSum += v * v;
                if (++blockCount < block) continue;
                loudness.Enqueue(new Loudness { Voice = voiceRead + i + 1 - block, Rms = (float)Math.Sqrt(blockSum / block) });
                blockSum = 0;
                blockCount = 0;
                if (loudness.Count > 50 * 120) loudness.Dequeue();
            }
        }

        /// <summary>Drops everything queued (the caller interrupted, or a newer reply began) and counts from 0 again.</summary>
        public void Clear()
        {
            lock (gate)
            {
                head = 0;
                count = 0;
                spans.Clear();
                loudness.Clear();
                voiceRead = heard = 0;
                clears++;
                blockSum = 0;
                blockCount = 0;
            }
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
