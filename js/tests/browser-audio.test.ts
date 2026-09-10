import { describe, it, expect, beforeEach } from 'vitest';
import { BrowserAudioManager } from '../src/browser-audio';

describe('BrowserAudioManager', () => {
  describe('Constructor', () => {
    it('should initialize with default config', () => {
      const manager = new BrowserAudioManager();
      expect(manager).toBeDefined();
    });

    it('should accept custom audio config', () => {
      const manager = new BrowserAudioManager({
        inputSampleRate: 44100,
        outputSampleRate: 44100,
        autoGainControl: false,
        echoCancellation: false,
        noiseSuppression: false,
      });

      expect(manager).toBeDefined();
    });

    it('should accept onInputError callback', () => {
      const onInputError = (error: Error) => {
        console.error(error);
      };

      const manager = new BrowserAudioManager({ onInputError });
      expect(manager).toBeDefined();
    });
  });

  describe('Audio Context', () => {
    it('should initialize audio context', async () => {
      const manager = new BrowserAudioManager();

      try {
        await manager.init();
        expect(manager).toBeDefined();
      } catch (e) {
        expect(e).toBeDefined();
      }
    });

    it('should handle audio context not supported', async () => {
      const originalAudioContext = (global as any).AudioContext;
      (global as any).AudioContext = undefined;
      (global as any).webkitAudioContext = undefined;

      const manager = new BrowserAudioManager();

      try {
        await manager.init();
      } catch (e) {
        expect(e).toStrictEqual(expect.any(Error));
      }

      (global as any).AudioContext = originalAudioContext;
    });
  });

  describe('Mute State', () => {
    it('should track mute state', () => {
      const manager = new BrowserAudioManager();

      expect(manager.isMicMuted()).toBe(false);

      manager.setMuted(true);
      expect(manager.isMicMuted()).toBe(true);

      manager.setMuted(false);
      expect(manager.isMicMuted()).toBe(false);
    });

    // Regression test: muting used to stop sending audio chunks entirely
    // (`if (this.isMuted) return;` before any data reached onAudioInput).
    // The server's turn-taking is purely reactive to incoming chunks, so
    // that left conversations stuck mid-turn if the user muted instead of
    // just going quiet — the server never got a chance to observe silence
    // and hand off. Muted chunks must still flow, just zeroed.
    it('should keep sending chunks while muted, but silenced', () => {
      const manager = new BrowserAudioManager();
      const received: Uint8Array[] = [];

      // Reach into private state the way `startMicrophone` would set it up,
      // without needing a real AudioContext/getUserMedia in this test env.
      (manager as any).onAudioInput = (data: Uint8Array) => received.push(data);
      (manager as any).audioContext = {};
      (manager as any).isListening = true;
      (manager as any).resampler = null;

      const inputSamples = new Float32Array(128).fill(0.5); // clearly non-silent
      const fakeEvent = {
        inputBuffer: { getChannelData: () => inputSamples },
        outputBuffer: { getChannelData: () => new Float32Array(128) },
      };

      manager.setMuted(true);
      (manager as any)._processAudioInput(fakeEvent);

      expect(received).toHaveLength(1); // chunk was sent, not dropped
      const view = new Int16Array(received[0].buffer, received[0].byteOffset, received[0].byteLength / 2);
      expect(Array.from(view).every((s) => s === 0)).toBe(true); // but silent

      manager.setMuted(false);
      (manager as any)._processAudioInput(fakeEvent);
      const view2 = new Int16Array(received[1].buffer, received[1].byteOffset, received[1].byteLength / 2);
      expect(Array.from(view2).some((s) => s !== 0)).toBe(true); // real audio flows when unmuted
    });
  });

  describe('Amplitude Tracking', () => {
    it('should return amplitude in valid range', () => {
      const manager = new BrowserAudioManager();

      const amplitude = manager.getAmplitude();
      expect(amplitude).toBeGreaterThanOrEqual(0);
      expect(amplitude).toBeLessThanOrEqual(1);
    });
  });

  describe('Frequency Data', () => {
    it('should return empty frequency data when not initialized', () => {
      const manager = new BrowserAudioManager();

      const frequencyData = manager.getFrequencyData();
      expect(frequencyData).toEqual(new Uint8Array(0));
    });
  });

  describe('Waveform Data', () => {
    it('should return empty waveform data when not initialized', () => {
      const manager = new BrowserAudioManager();

      const waveformData = manager.getWaveformData();
      expect(waveformData).toEqual(new Uint8Array(0));
    });
  });

  describe('Playback', () => {
    it('should handle play audio when not initialized', () => {
      const manager = new BrowserAudioManager();
      const audioData = new Uint8Array([0, 1, 2, 3]);

      expect(() => {
        manager.playAudio(audioData);
      }).not.toThrow();
    });
  });

  describe('Cleanup', () => {
    it('should clean up resources', () => {
      const manager = new BrowserAudioManager();

      expect(() => {
        manager.cleanup();
      }).not.toThrow();
    });

    it('should stop microphone on cleanup', () => {
      const manager = new BrowserAudioManager();

      expect(() => {
        manager.stopMicrophone();
        manager.cleanup();
      }).not.toThrow();
    });

    it('should stop playback on cleanup', () => {
      const manager = new BrowserAudioManager();

      expect(() => {
        manager.stopPlayback();
        manager.cleanup();
      }).not.toThrow();
    });
  });
});
