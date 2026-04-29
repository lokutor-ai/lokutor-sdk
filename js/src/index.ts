export * from './types';
export * from './client';
export * from './audio-utils';
export * from './browser-audio';
export { VoiceAgentClient, TTSClient, simpleConversation, simpleTTS } from './client';
export { BrowserAudioManager } from './browser-audio';
export {
  pcm16ToFloat32,
  float32ToPcm16,
  resample,
  resampleWithAntiAliasing,
  calculateRMS,
  normalizeAudio,
  StreamResampler,
} from './audio-utils';
export type {
  VoiceAgentOptions,
  Viseme,
  VoiceInfo,
  LanguageInfo,
  ModelInfo,
  ServerConfig,
  ServerStatus,
  HealthStatus,
} from './types';
