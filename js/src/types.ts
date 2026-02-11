/**
 * Available voice styles for the Lokutor AI Agent
 */
export enum VoiceStyle {
  // Female voices
  F1 = "F1",
  F2 = "F2",
  F3 = "F3",
  F4 = "F4",
  F5 = "F5",

  // Male voices
  M1 = "M1",
  M2 = "M2",
  M3 = "M3",
  M4 = "M4",
  M5 = "M5",
}

/**
 * Supported languages for speech and text
 */
export enum Language {
  ENGLISH = "en",
  SPANISH = "es",
  FRENCH = "fr",
  PORTUGUESE = "pt",
  KOREAN = "ko",
}

/**
 * Audio configuration constants
 */
export const AUDIO_CONFIG = {
  SAMPLE_RATE: 44100,
  CHANNELS: 1,
  CHUNK_DURATION_MS: 20,
  get CHUNK_SIZE() {
    return Math.floor((this.SAMPLE_RATE * this.CHUNK_DURATION_MS) / 1000);
  }
};

/**
 * Default WebSocket URLs
 */
export const DEFAULT_URLS = {
  VOICE_AGENT: "wss://api.lokutor.com/ws/agent",
  TTS: "wss://api.lokutor.com/ws/tts",
};

/**
 * SDK Configuration interface
 */
export interface LokutorConfig {
  apiKey: string;
  serverUrl?: string;
  onTranscription?: (text: string) => void;
  onResponse?: (text: string) => void;
  onAudio?: (data: Uint8Array) => void;
  onStatus?: (status: string) => void;
  onError?: (error: any) => void;
}

/**
 * Text-to-Speech synthesis request options
 */
export interface SynthesizeOptions {
  text: string;
  voice?: VoiceStyle;
  language?: Language;
  speed?: number;
  steps?: number;
  visemes?: boolean;
}
