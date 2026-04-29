import { describe, it, expect, beforeEach } from 'vitest';
import { VoiceStyle, Language, AUDIO_CONFIG } from '../src/types';

describe('SDK Types and Enums', () => {
  describe('VoiceStyle enum', () => {
    it('should have female voices', () => {
      expect(VoiceStyle.F1).toBe('F1');
      expect(VoiceStyle.F2).toBe('F2');
      expect(VoiceStyle.F3).toBe('F3');
      expect(VoiceStyle.F4).toBe('F4');
      expect(VoiceStyle.F5).toBe('F5');
    });

    it('should have male voices', () => {
      expect(VoiceStyle.M1).toBe('M1');
      expect(VoiceStyle.M2).toBe('M2');
      expect(VoiceStyle.M3).toBe('M3');
      expect(VoiceStyle.M4).toBe('M4');
      expect(VoiceStyle.M5).toBe('M5');
    });

    it('should have 10 voices total', () => {
      const voices = Object.values(VoiceStyle);
      expect(voices).toHaveLength(10);
    });
  });

  describe('Language enum', () => {
    it('should support English', () => {
      expect(Language.ENGLISH).toBe('en');
    });

    it('should support Spanish', () => {
      expect(Language.SPANISH).toBe('es');
    });

    it('should support French', () => {
      expect(Language.FRENCH).toBe('fr');
    });

    it('should support Portuguese', () => {
      expect(Language.PORTUGUESE).toBe('pt');
    });

    it('should support Korean', () => {
      expect(Language.KOREAN).toBe('ko');
    });

    it('should have 5 languages total', () => {
      const languages = Object.values(Language);
      expect(languages).toHaveLength(5);
    });
  });

  describe('AUDIO_CONFIG', () => {
    it('should have valid sample rates', () => {
      expect(AUDIO_CONFIG.SAMPLE_RATE).toBe(16000);
      expect(AUDIO_CONFIG.SAMPLE_RATE_INPUT).toBe(16000);
      expect(AUDIO_CONFIG.SPEAKER_SAMPLE_RATE).toBe(44100);
      expect(AUDIO_CONFIG.SAMPLE_RATE_OUTPUT).toBe(44100);
    });

    it('should have mono audio', () => {
      expect(AUDIO_CONFIG.CHANNELS).toBe(1);
    });

    it('should calculate chunk size correctly', () => {
      const expectedSize = Math.floor((16000 * 20) / 1000);
      expect(AUDIO_CONFIG.CHUNK_SIZE).toBe(expectedSize);
    });

    it('should have chunk duration', () => {
      expect(AUDIO_CONFIG.CHUNK_DURATION_MS).toBe(20);
    });
  });
});
