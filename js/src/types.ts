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
  GERMAN = "de",
  ITALIAN = "it",
  PORTUGUESE = "pt",
  JAPANESE = "ja",
  KOREAN = "ko",
  CHINESE = "zh",
  ARABIC = "ar",
  BULGARIAN = "bg",
  CROATIAN = "hr",
  CZECH = "cs",
  DANISH = "da",
  DUTCH = "nl",
  ESTONIAN = "et",
  FINNISH = "fi",
  GREEK = "el",
  HINDI = "hi",
  HUNGARIAN = "hu",
  INDONESIAN = "id",
  LATVIAN = "lv",
  LITHUANIAN = "lt",
  POLISH = "pl",
  ROMANIAN = "ro",
  RUSSIAN = "ru",
  SLOVAK = "sk",
  SLOVENIAN = "sl",
  SWEDISH = "sv",
  TURKISH = "tr",
  UKRAINIAN = "uk",
  VIETNAMESE = "vi",
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
  STT: "wss://api.lokutor.com/ws/stt",
};

/**
 * SDK Configuration interface
 */
export interface LokutorConfig {
  apiKey: string;
  agentId?: string;
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
 * Options for one-shot batch transcription via STTClient.transcribe()
 * (POST /stt/transcribe) — pass a complete recording and get a transcript
 * back. For continuous/live transcription, use SpeechToTextClient instead.
 */
export interface TranscribeOptions {
  /** Complete audio to transcribe. A Blob/File (e.g. from a file input or
   *  MediaRecorder) is sent as multipart/form-data; a raw PCM16 buffer is
   *  sent as base64 JSON with `format: "pcm16"`. */
  audio: Blob | ArrayBuffer | Uint8Array;
  /** Required when `audio` is raw PCM16 (ignored for WAV/Blob input, whose
   *  rate is read from the file header). */
  sampleRate?: number;
  /** Set when passing a raw PCM16 buffer instead of a WAV Blob. */
  format?: 'wav' | 'pcm16';
  language?: Language;
}

/** One transcribed segment with timing, when the engine provides them. */
export interface TranscribeSegment {
  text: string;
  start: number;
  end: number;
  duration: number;
}

/** Result of STTClient.transcribe() — mirrors POST /stt/transcribe's response body. */
export interface TranscribeResult {
  text: string;
  latencyMs: number;
  engine: string;
  sampleRate: number;
  language: string;
  durationSeconds: number;
  segments?: TranscribeSegment[];
}

/**
 * Continuous speech-to-text options (WS /ws/stt) — standalone transcription
 * with server-side VAD, independent of the full voice-agent pipeline (no
 * LLM turn, no TTS). Use this for live captioning/dictation; use
 * STTClient.transcribe() for a single pre-recorded clip.
 */
export interface SpeechToTextOptions {
  apiKey: string;
  serverUrl?: string;
  language?: Language;
  /** VAD engine: "silero" (default, neural) or "rms" (energy-threshold fallback). */
  vad?: 'silero' | 'rms';
  /** Fires repeatedly while the user is mid-utterance, with the best partial guess so far. */
  onPartialTranscript?: (text: string) => void;
  /** Fires once VAD detects the utterance ended. */
  onFinalTranscript?: (text: string) => void;
  onError?: (error: LokutorError) => void;
  onStatusChange?: (status: 'connecting' | 'connected' | 'disconnected') => void;
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
 * REST API response types for discovery endpoints.
 */

export interface VoiceInfo {
  id: string;
  gender?: string;
  languages?: string[];
}

export interface LanguageInfo {
  code: string;
  name: string;
}

export interface ModelInfo {
  name: string;
  description?: string;
  default?: boolean;
}

export interface ServerConfig {
  max_text_length: number;
  min_speed: number;
  max_speed: number;
  min_steps: number;
  max_steps: number;
  sample_rate: number;
  channels: number;
}

export interface ServerStatus {
  status: string;
  timestamp: string;
  version: string;
  runtime: string;
  inference: string;
  uptime_seconds: number;
  goroutines: number;
  mem_alloc_bytes: number;
  active_connections: number;
  failed_requests: number;
  ready: boolean;
}

export interface HealthStatus {
  status: string;
  timestamp: string;
  version: string;
  runtime: string;
  inference: string;
  load: number;
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
