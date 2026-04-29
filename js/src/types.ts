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
  SAMPLE_RATE: 16000,
  SAMPLE_RATE_INPUT: 16000,
  SPEAKER_SAMPLE_RATE: 44100,
  SAMPLE_RATE_OUTPUT: 44100,
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

/**
 * Browser audio configuration options
 */
export interface BrowserAudioOptions {
  inputSampleRate?: number;
  outputSampleRate?: number;
  autoGainControl?: boolean;
  echoCancellation?: boolean;
  noiseSuppression?: boolean;
  analyserEnabled?: boolean;
  onInputError?: (error: Error) => void;
}

/**
 * Voice agent conversation options
 */
export interface VoiceAgentOptions {
  prompt?: string;
  voice?: VoiceStyle;
  language?: Language;
  serverUrl?: string;
  visemes?: boolean;
  onTranscription?: (text: string) => void;
  onVisemes?: (visemes: Viseme[]) => void;
  onStatusChange?: (status: string) => void;
  onError?: (err: LokutorError) => void;
}

/**
 * Viseme data for lip-sync animation
 * Format: {"v": index, "c": character, "t": timestamp}
 */
export interface Viseme {
  /** Text-position index (which input character the model is attending to), not a stable viseme ID */
  v: number;
  /** Character/phoneme being spoken (reduced set: a,e,i,o,u,m,p,b,f,v,t,d,s,z,l,n,r,k,g,sil) */
  c: string;
  /** Offset in seconds from the start of the audio stream */
  t: number;
}

/**
 * Tool definition for LLM function calling (OpenAI format)
 */
export interface ToolDefinition {
  type: 'function';
  function: {
    name: string;
    description: string;
    parameters: {
      type: 'object';
      properties: Record<string, any>;
      required?: string[];
    };
  };
}

/**
 * Event data for tool execution
 */
export interface ToolCall {
  name: string;
  arguments: string;
}

/**
 * Error code enum matching the backend API error catalog
 */
export type ErrorCode =
  | 'auth.missing_key'
  | 'auth.invalid_key'
  | 'auth.rate_limited'
  | 'auth.time_limited'
  | 'validation.invalid_voice'
  | 'validation.invalid_language'
  | 'validation.text_too_long'
  | 'validation.speed_out_of_range'
  | 'validation.steps_out_of_range'
  | 'validation.invalid_request_format'
  | 'tts.synthesis_failed'
  | 'tts.voice_unavailable'
  | 'tts.model_not_found'
  | 'tts.session_limit_reached'
  | 'stt.not_configured'
  | 'stt.stream_create_failed'
  | 'stt.language_not_supported'
  | 'agent.session_failed'
  | 'agent.provider_error'
  | 'internal.error'
  | 'internal.timeout'
  | 'internal.cancelled'
  | 'ws.close';

/**
 * Typed error class for all Lokutor SDK errors.
 * Includes the backend error code, human-readable message,
 * optional detail, and whether the operation is retryable.
 */
export class LokutorError extends Error {
  public readonly code: ErrorCode;
  public readonly detail?: string;
  public readonly retryable: boolean;
  public readonly original?: unknown;

  constructor(code: ErrorCode, message: string, opts?: { detail?: string; retryable?: boolean; original?: unknown }) {
    super(message);
    this.name = 'LokutorError';
    this.code = code;
    this.detail = opts?.detail;
    this.retryable = opts?.retryable ?? isRetryableCode(code);
    this.original = opts?.original;
  }
}

function isRetryableCode(code: ErrorCode): boolean {
  const fatal: ErrorCode[] = [
    'auth.missing_key',
    'auth.invalid_key',
    'auth.time_limited',
    'validation.invalid_voice',
    'validation.invalid_language',
    'validation.text_too_long',
    'validation.speed_out_of_range',
    'validation.steps_out_of_range',
    'validation.invalid_request_format',
    'internal.cancelled',
  ];
  return !fatal.includes(code);
}

/**
 * Returns true if the given error is a retryable LokutorError.
 */
export function isRetryable(error: unknown): boolean {
  if (error instanceof LokutorError) {
    return error.retryable;
  }
  return false;
}
