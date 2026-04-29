import { AUDIO_CONFIG } from './types';

/**
 * Audio utility functions for format conversion, resampling, and PCM processing
 */

/**
 * Convert 16-bit PCM (Int16) to 32-bit Float
 * @param int16Data Int16Array of PCM audio
 * @returns Float32Array normalized to [-1, 1]
 */
export function pcm16ToFloat32(int16Data: Int16Array): Float32Array {
  const float32 = new Float32Array(int16Data.length);
  for (let i = 0; i < int16Data.length; i++) {
    float32[i] = int16Data[i] / 32768.0;
  }
  return float32;
}

/**
 * Convert 32-bit Float to 16-bit PCM (Int16)
 * @param float32Data Float32Array normalized to [-1, 1]
 * @returns Int16Array of PCM audio
 */
export function float32ToPcm16(float32Data: Float32Array): Int16Array {
  const int16 = new Int16Array(float32Data.length);
  for (let i = 0; i < float32Data.length; i++) {
    // Clamp to [-1, 1]
    const s = Math.max(-1, Math.min(1, float32Data[i]));
    // Convert to Int16
    int16[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
  }
  return int16;
}

/**
 * Resample audio data from one sample rate to another using linear interpolation
 * @param input Float32Array of input audio
 * @param inputRate Original sample rate in Hz
 * @param outputRate Target sample rate in Hz
 * @returns Float32Array of resampled audio
 */
export function resample(
  input: Float32Array,
  inputRate: number,
  outputRate: number
): Float32Array {
  if (inputRate === outputRate) {
    return new Float32Array(input);
  }

  const ratio = inputRate / outputRate;
  const outputLength = Math.round(input.length / ratio);
  const output = new Float32Array(outputLength);

  for (let i = 0; i < outputLength; i++) {
    const pos = i * ratio;
    const left = Math.floor(pos);
    const right = Math.min(left + 1, input.length - 1);
    const weight = pos - left;

    // Linear interpolation
    output[i] = input[left] * (1 - weight) + input[right] * weight;
  }

  return output;
}

/**
 * Apply a simple low-pass filter for anti-aliasing during downsampling
 * @param data Float32Array of audio
 * @param cutoffFreq Cutoff frequency in Hz
 * @param sampleRate Sample rate in Hz
 * @returns Filtered Float32Array
 */
export function applyLowPassFilter(
  data: Float32Array,
  cutoffFreq: number,
  sampleRate: number
): Float32Array {
  // Calculate filter coefficient using first-order IIR filter
  const dt = 1 / sampleRate;
  const rc = 1 / (2 * Math.PI * cutoffFreq);
  const alpha = dt / (rc + dt);

  const filtered = new Float32Array(data.length);
  filtered[0] = data[0];

  for (let i = 1; i < data.length; i++) {
    filtered[i] = filtered[i - 1] + alpha * (data[i] - filtered[i - 1]);
  }

  return filtered;
}

/**
 * Resample audio with anti-aliasing low-pass filter
 * Best used when downsampling to prevent aliasing artifacts
 * @param input Float32Array of input audio
 * @param inputRate Original sample rate in Hz
 * @param outputRate Target sample rate in Hz
 * @returns Float32Array of resampled and filtered audio
 */
export function resampleWithAntiAliasing(
  input: Float32Array,
  inputRate: number,
  outputRate: number
): Float32Array {
  if (inputRate === outputRate) {
    return new Float32Array(input);
  }

  // If downsampling, apply low-pass filter first to prevent aliasing
  let processed = input;
  if (outputRate < inputRate) {
    const nyquistFreq = outputRate / 2;
    const cutoffFreq = nyquistFreq * 0.9; // 90% of Nyquist to be safe
    processed = applyLowPassFilter(input, cutoffFreq, inputRate);
  }

  return resample(processed, inputRate, outputRate);
}

/**
 * Convert raw audio samples to Uint8Array (bytes)
 * @param data Int16Array of PCM audio
 * @returns Uint8Array containing PCM bytes
 */
export function pcm16ToBytes(data: Int16Array): Uint8Array {
  return new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
}

/**
 * Convert bytes to Int16Array
 * @param bytes Uint8Array of PCM bytes
 * @returns Int16Array of PCM audio
 */
export function bytesToPcm16(bytes: Uint8Array): Int16Array {
  if (bytes.length % 2 !== 0) {
    // Trim the last byte to avoid RangeError
    bytes = bytes.slice(0, bytes.length - 1);
  }
  return new Int16Array(bytes.buffer, bytes.byteOffset, bytes.length / 2);
}

/**
 * Normalize audio amplitude to prevent clipping
 * @param data Float32Array of audio
 * @param targetPeak Peak level to normalize to (0-1)
 * @returns Normalized Float32Array
 */
export function normalizeAudio(data: Float32Array, targetPeak: number = 0.95): Float32Array {
  let maxAbs = 0;
  for (let i = 0; i < data.length; i++) {
    maxAbs = Math.max(maxAbs, Math.abs(data[i]));
  }

  if (maxAbs === 0) return new Float32Array(data);

  const scale = targetPeak / maxAbs;
  const normalized = new Float32Array(data.length);
  for (let i = 0; i < data.length; i++) {
    normalized[i] = data[i] * scale;
  }
  return normalized;
}

/**
 * Calculate RMS (Root Mean Square) amplitude
 * @param data Float32Array or Uint8Array of audio
 * @returns RMS value (0-1 for normalized float, 0-255 for byte data)
 */
export function calculateRMS(data: Float32Array | Uint8Array): number {
  let sum = 0;
  let length = data.length;

  if (data instanceof Uint8Array) {
    // For byte data (0-128 is silence, 128-255 is audio)
    for (let i = 0; i < length; i++) {
      const v = (data[i] - 128) / 128.0;
      sum += v * v;
    }
  } else {
    // For float data (-1 to 1)
    for (let i = 0; i < length; i++) {
      sum += data[i] * data[i];
    }
  }

  return Math.sqrt(sum / length);
}

/**
 * Create a resample function factory for streaming audio
 * Useful for processing audio in chunks
 */
export class StreamResampler {
  private inputBuffer: Float32Array = new Float32Array(0);
  private inputRate: number;
  private outputRate: number;

  constructor(inputRate: number, outputRate: number) {
    this.inputRate = inputRate;
    this.outputRate = outputRate;
  }

  /**
   * Process a chunk of audio and return resampled data
   * @param inputChunk Float32Array chunk to process
   * @param flush If true, output remaining buffered samples
   * @returns Resampled Float32Array (may be empty if more data needed)
   */
  process(inputChunk: Float32Array, flush: boolean = false): Float32Array {
    // Concatenate new data with buffered data
    const combined = new Float32Array(this.inputBuffer.length + inputChunk.length);
    combined.set(this.inputBuffer);
    combined.set(inputChunk, this.inputBuffer.length);

    const ratio = this.inputRate / this.outputRate;
    let outputLength = Math.floor(combined.length / ratio);

    if (outputLength === 0 && !flush) {
      this.inputBuffer = combined;
      return new Float32Array(0);
    }

    // On flush, output everything including partial frames
    if (flush && outputLength === 0 && combined.length > 0) {
      outputLength = 1;
    }

    const output = new Float32Array(outputLength);

    for (let i = 0; i < outputLength; i++) {
      const pos = i * ratio;
      const left = Math.floor(pos);
      const right = Math.min(left + 1, combined.length - 1);
      const weight = pos - left;

      output[i] = combined[left] * (1 - weight) + combined[right] * weight;
    }

    // Keep remaining samples in buffer
    const consumed = Math.floor(outputLength * ratio);
    this.inputBuffer = combined.slice(consumed);

    return output;
  }

  reset(): void {
    this.inputBuffer = new Float32Array(0);
  }
}
