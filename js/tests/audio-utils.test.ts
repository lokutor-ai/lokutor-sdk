import { describe, it, expect, beforeEach } from 'vitest';
import {
  pcm16ToFloat32,
  float32ToPcm16,
  resample,
  normalizeAudio,
  calculateRMS,
  StreamResampler,
} from '../src/audio-utils';

describe('Audio Utilities', () => {
  describe('PCM16 to Float32 conversion', () => {
    it('should convert PCM16 to Float32', () => {
      const pcm16 = new Int16Array([0, 32767, -32768, 16384]);
      const float32 = pcm16ToFloat32(pcm16);

      expect(float32).toHaveLength(4);
      expect(float32[0]).toBe(0);
      expect(float32[1]).toBeCloseTo(1.0, 4);
      expect(float32[2]).toBeCloseTo(-1.0, 4);
      expect(float32[3]).toBeCloseTo(0.5, 4);
    });

    it('should handle empty PCM16 data', () => {
      const pcm16 = new Int16Array(0);
      const float32 = pcm16ToFloat32(pcm16);
      expect(float32).toHaveLength(0);
    });
  });

  describe('Float32 to PCM16 conversion', () => {
    it('should convert Float32 to PCM16', () => {
      const float32 = new Float32Array([0, 1.0, -1.0, 0.5]);
      const pcm16 = float32ToPcm16(float32);

      expect(pcm16).toHaveLength(4);
      expect(pcm16[0]).toBe(0);
      expect(pcm16[1]).toBe(32767);
      expect(pcm16[2]).toBe(-32768);
      expect(pcm16[3]).toBe(16383);
    });

    it('should clamp values to [-1, 1]', () => {
      const float32 = new Float32Array([1.5, -1.5, 2.0]);
      const pcm16 = float32ToPcm16(float32);

      expect(pcm16[0]).toBe(32767);
      expect(pcm16[1]).toBe(-32768);
      expect(pcm16[2]).toBe(32767);
    });

    it('should be inverse of pcm16ToFloat32', () => {
      const originalPcm = new Int16Array([0, 1000, -2000, 15000]);
      const float32 = pcm16ToFloat32(originalPcm);
      const reconstructed = float32ToPcm16(float32);

      for (let i = 0; i < originalPcm.length; i++) {
        expect(Math.abs(reconstructed[i] - originalPcm[i])).toBeLessThanOrEqual(1);
      }
    });
  });

  describe('Resampling', () => {
    it('should resample audio from 48kHz to 16kHz', () => {
      const input = new Float32Array(48);
      for (let i = 0; i < 48; i++) {
        input[i] = Math.sin((i / 48) * 2 * Math.PI);
      }

      const output = resample(input, 48000, 16000);
      expect(output.length).toBeCloseTo(16, 0);
    });

    it('should return same data if rates match', () => {
      const input = new Float32Array([0.1, 0.2, 0.3]);
      const output = resample(input, 16000, 16000);

      expect(output).toEqual(input);
    });

    it('should use linear interpolation', () => {
      const input = new Float32Array([0, 1, 0]);
      const output = resample(input, 3, 6);

      expect(output.length).toBeCloseTo(6, 0);
      expect(output[0]).toBeCloseTo(0, 1);
      expect(output[output.length - 1]).toBeCloseTo(0, 1);
    });

    it('should handle upsampling', () => {
      const input = new Float32Array([0, 1, 0]);
      const output = resample(input, 8000, 16000);

      expect(output.length).toBeCloseTo(6, 0);
    });

    it('should handle downsampling', () => {
      const input = new Float32Array(16);
      for (let i = 0; i < 16; i++) {
        input[i] = Math.random();
      }

      const output = resample(input, 16000, 8000);
      expect(output.length).toBeCloseTo(8, 0);
    });
  });

  describe('Audio normalization', () => {
    it('should normalize audio to target peak', () => {
      const input = new Float32Array([0.2, 0.4, 0.3]);
      const output = normalizeAudio(input, 1.0);

      const maxAbs = Math.max(...Array.from(output).map(Math.abs));
      expect(maxAbs).toBeCloseTo(1.0, 5);
    });

    it('should handle zero-amplitude input', () => {
      const input = new Float32Array([0, 0, 0]);
      const output = normalizeAudio(input, 0.95);

      expect(output).toEqual(input);
    });

    it('should not modify already normalized audio', () => {
      const input = new Float32Array([0.1, 0.5, 0.3]);
      const output = normalizeAudio(input, 0.95);

      const maxAbs = Math.max(...Array.from(output).map(Math.abs));
      expect(maxAbs).toBeLessThanOrEqual(0.95);
    });
  });

  describe('RMS calculation', () => {
    it('should calculate RMS for Float32 data', () => {
      const input = new Float32Array([0, 1, 0, -1]);
      const rms = calculateRMS(input);

      expect(rms).toBeCloseTo(Math.sqrt(0.5), 5);
    });

    it('should calculate RMS for Uint8 data', () => {
      const input = new Uint8Array([128, 255, 128, 0]);
      const rms = calculateRMS(input);

      expect(rms).toBeGreaterThanOrEqual(0);
    });

    it('should return 0 for silent audio', () => {
      const input = new Float32Array([0, 0, 0, 0]);
      const rms = calculateRMS(input);

      expect(rms).toBe(0);
    });

    it('should return 1 for normalized signal', () => {
      const input = new Float32Array([1, 1, 1, 1]);
      const rms = calculateRMS(input);

      expect(rms).toBe(1);
    });
  });

  describe('StreamResampler', () => {
    it('should resample streaming audio chunks', () => {
      const resampler = new StreamResampler(48000, 16000);

      const chunk1 = new Float32Array(48);
      const chunk2 = new Float32Array(48);

      const output1 = resampler.process(chunk1);
      const output2 = resampler.process(chunk2);

      expect(output1.length + output2.length).toBeCloseTo(32, 0);
    });

    it('should buffer incomplete samples', () => {
      const resampler = new StreamResampler(48000, 16000);

      const chunk = new Float32Array(10);
      const output = resampler.process(chunk);

      expect(output.length).toBeCloseTo(3, 1);
    });

    it('should flush remaining samples', () => {
      const resampler = new StreamResampler(48000, 16000);

      const chunk = new Float32Array(50);
      const output1 = resampler.process(chunk, false);
      expect(output1.length).toBeLessThan(50);
    });

    it('should reset state', () => {
      const resampler = new StreamResampler(48000, 16000);

      const chunk1 = new Float32Array(48);
      resampler.process(chunk1);

      resampler.reset();

      const chunk2 = new Float32Array(48);
      const output = resampler.process(chunk2);

      expect(output.length).toBeCloseTo(16, 0);
    });

    it('should maintain continuity across chunks', () => {
      const resampler = new StreamResampler(8000, 4000);

      const allSamples = new Float32Array(16);
      for (let i = 0; i < 16; i++) {
        allSamples[i] = Math.sin((i / 16) * 2 * Math.PI);
      }

      const chunk1 = allSamples.slice(0, 8);
      const chunk2 = allSamples.slice(8);

      const output1 = resampler.process(chunk1);
      const output2 = resampler.process(chunk2);

      const combinedOutput = new Float32Array(output1.length + output2.length);
      combinedOutput.set(output1);
      combinedOutput.set(output2, output1.length);

      expect(combinedOutput.length).toBeCloseTo(8, 0);
    });
  });

  // The microphone (48 kHz on phones, 44.1 kHz on some laptops) down to the recogniser's 16 kHz.
  describe('StreamResampler downsampling (anti-aliasing filter)', () => {
    const tone = (freq: number, rate: number, seconds: number, amplitude = 0.3) => {
      const x = new Float32Array(Math.round(rate * seconds));
      for (let i = 0; i < x.length; i++) x[i] = amplitude * Math.sin((2 * Math.PI * freq * i) / rate);
      return x;
    };
    const noise = (length: number) => {
      let s = 12345;
      const x = new Float32Array(length);
      for (let i = 0; i < length; i++) {
        s = (Math.imul(s, 1103515245) + 12345) >>> 0;
        x[i] = (s / 2 ** 32) * 2 - 1;
      }
      return x;
    };
    // Amplitude of `freq` in a 16 kHz signal: one DFT bin over 100 ms (whole cycles at 1 and 4 kHz),
    // after the filter has settled.
    const amplitudeAt = (y: Float32Array, freq: number, skip = 200, n = 1600) => {
      let re = 0;
      let im = 0;
      for (let i = 0; i < n; i++) {
        const t = (2 * Math.PI * freq * i) / 16000;
        re += y[skip + i] * Math.cos(t);
        im -= y[skip + i] * Math.sin(t);
      }
      return (2 * Math.hypot(re, im)) / n;
    };
    // Feeds x in blocks, cycling through `sizes`, and joins the outputs.
    const inBlocks = (r: StreamResampler, x: Float32Array, sizes: number[], flush = false) => {
      const parts: Float32Array[] = [];
      for (let i = 0, b = 0; i < x.length; b++) {
        const size = sizes[b % sizes.length];
        parts.push(r.process(x.subarray(i, i + size)));
        i += size;
      }
      if (flush) parts.push(r.process(new Float32Array(0), true));
      const y = new Float32Array(parts.reduce((sum, p) => sum + p.length, 0));
      let offset = 0;
      for (const p of parts) {
        y.set(p, offset);
        offset += p.length;
      }
      return y;
    };

    for (const rate of [48000, 44100]) {
      describe(`${rate / 1000} kHz to 16 kHz`, () => {
        it('leaves nothing of a 12 kHz tone at its 4 kHz alias', () => {
          const x = tone(12000, rate, 0.5);
          const y = inBlocks(new StreamResampler(rate, 16000), x, [1024]);
          // Below -60 dB (measured -85 dB at 48 kHz, -92 dB at 44.1 kHz).
          expect(amplitudeAt(y, 4000)).toBeLessThan(0.3e-3);
          // Control: linear interpolation alone, as before 1.6.1, folds it back into the speech band.
          expect(amplitudeAt(resample(x, rate, 16000), 4000)).toBeGreaterThan(0.1);
        });

        it('keeps a 1 kHz tone at full amplitude', () => {
          const y = inBlocks(new StreamResampler(rate, 16000), tone(1000, rate, 0.5), [1024]);
          expect(Math.abs(amplitudeAt(y, 1000) - 0.3)).toBeLessThan(0.003);
        });

        it('gives exactly the whole-signal output however the input is split', () => {
          const x = noise(rate * 2);
          const whole = new StreamResampler(rate, 16000).process(x);
          for (const sizes of [[1024], [4096], [1, 7, 128, 333, 4096, 2, 0, 1000]]) {
            expect(inBlocks(new StreamResampler(rate, 16000), x, sizes)).toEqual(whole);
          }
          const wholeFlushed = new StreamResampler(rate, 16000).process(x, true);
          expect(inBlocks(new StreamResampler(rate, 16000), x, [1024], true)).toEqual(wholeFlushed);
        });

        it('keeps time over a long stream: 31 input samples of filter delay and no drift', () => {
          const seconds = 5;
          const y = inBlocks(new StreamResampler(rate, 16000), tone(1000, rate, seconds), [1024]);
          expect(Math.abs(y.length - 16000 * seconds)).toBeLessThanOrEqual(1);
          let worst = 0;
          for (let n = 100; n < y.length; n++) {
            const t = (n * rate) / 16000 - (StreamResampler.FILTER_TAPS - 1) / 2;
            worst = Math.max(worst, Math.abs(y[n] - 0.3 * Math.sin((2 * Math.PI * 1000 * t) / rate)));
          }
          expect(worst).toBeLessThan(0.003);
        });
      });
    }

    it('costs well under 1% of real time per 4096-sample block', () => {
      for (const rate of [48000, 44100]) {
        const block = noise(4096);
        const r = new StreamResampler(rate, 16000);
        for (let i = 0; i < 100; i++) r.process(block);
        const blocks = 500;
        const start = performance.now();
        for (let i = 0; i < blocks; i++) r.process(block);
        const msPerBlock = (performance.now() - start) / blocks;
        const blockMs = (4096 / rate) * 1000;
        // Measured 0.06 ms (48 kHz) and 0.14 ms (44.1 kHz) per 85-93 ms block on an Apple M-series
        // core; the bound leaves room for slow CI machines and phones.
        expect(msPerBlock).toBeLessThan(blockMs * 0.02);
      }
    });

    it('drains the filter on flush, then starts over', () => {
      const x = tone(1000, 48000, 0.1);
      const r = new StreamResampler(48000, 16000);
      const y = r.process(x, true);
      // Runs on to the last input sample's own position, (taps - 1) / 2 samples past the end.
      expect(y.length).toBe(Math.ceil((x.length + (StreamResampler.FILTER_TAPS - 1) / 2) / 3));
      expect(Math.max(...Array.from(y.subarray(-5), Math.abs))).toBeGreaterThan(0.1);
      expect(r.process(x, true)).toEqual(y);
      expect(r.process(new Float32Array(0), true)).toHaveLength(0);
      expect(new StreamResampler(48000, 16000).process(new Float32Array(0), true)).toHaveLength(0);
    });

    it('leaves upsampling and equal rates on plain linear interpolation', () => {
      const ramp = new Float32Array([0, 1, 2, 3, 4, 5, 6, 7]);
      expect(new StreamResampler(8000, 16000).process(ramp)).toEqual(
        new Float32Array([0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 6.5, 7, 7])
      );
      expect(new StreamResampler(16000, 16000).process(ramp)).toEqual(ramp);
    });
  });
});
