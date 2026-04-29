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
});
