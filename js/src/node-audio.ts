/* eslint-disable @typescript-eslint/no-unused-vars */

import { AudioManager } from './client';
import { AUDIO_CONFIG } from './types';

/**
 * Node.js-specific AudioManager implementation.
 * Note: These require 'speaker' and 'node-record-lpcm16' to be installed by the user.
 */
export class NodeAudioManager implements AudioManager {
  private speaker: any = null;
  private recorder: any = null;
  private recordingStream: any = null;
  private isMuted: boolean = false;
  private isListening: boolean = false;

  constructor() {}

  async init(): Promise<void> {
    try {
      // Dynamic imports to avoid crashing if dependencies are missing at build time
      // The user must install these manually for managed Node.js audio
      const Speaker = await import('speaker').catch(() => null);
      if (!Speaker) {
        console.warn('⚠️  Package "speaker" is missing. Hardware output will be disabled.');
        console.warn('👉 Run: npm install speaker');
      }
    } catch (e) {
      console.error('Error initializing Node audio:', e);
    }
  }

  async startMicrophone(onAudioInput: (pcm16Data: Uint8Array) => void): Promise<void> {
    if (this.isListening) return;

    try {
      const recorder = await import('node-record-lpcm16').catch(() => null);
      if (!recorder) {
        throw new Error('Package "node-record-lpcm16" is missing. Microphone input failed.\n👉 Run: npm install node-record-lpcm16');
      }

      console.log('🎤 Starting microphone (Node.js)...');
      
      this.recordingStream = recorder.record({
        sampleRate: AUDIO_CONFIG.SAMPLE_RATE,
        threshold: 0,
        verbose: false,
        recordProgram: 'sox', // default
      });

      this.recordingStream.stream().on('data', (chunk: Buffer) => {
        if (!this.isMuted && onAudioInput) {
          onAudioInput(new Uint8Array(chunk));
        }
      });

      this.isListening = true;
    } catch (e: any) {
      console.error('Failed to start microphone:', e.message);
      throw e;
    }
  }

  stopMicrophone(): void {
    if (this.recordingStream) {
      this.recordingStream.stop();
      this.recordingStream = null;
    }
    this.isListening = false;
  }

  async playAudio(pcm16Data: Uint8Array): Promise<void> {
    try {
      if (!this.speaker) {
        const Speaker = (await import('speaker')).default;
        this.speaker = new Speaker({
          channels: AUDIO_CONFIG.CHANNELS,
          bitDepth: 16,
          sampleRate: AUDIO_CONFIG.SPEAKER_SAMPLE_RATE,
        });
      }

      // Node.js 'speaker' accepts Buffers
      this.speaker.write(Buffer.from(pcm16Data));
    } catch (e) {
      console.error('NodeAudioManager: speaker playback failed:', e);
    }
  }

  stopPlayback(): void {
    if (this.speaker) {
      this.speaker.end();
      this.speaker = null;
    }
  }

  cleanup(): void {
    this.stopMicrophone();
    this.stopPlayback();
  }

  isMicMuted(): boolean {
    return this.isMuted;
  }

  setMuted(muted: boolean): void {
    this.isMuted = muted;
  }

  getAmplitude(): number {
    // Amplitude tracking implementation for Node.js would require extra processing
    // leaving as stub for now
    return 0;
  }
}
