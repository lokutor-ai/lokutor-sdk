/**
 * Available voice styles for the Lokutor AI Agent
 */
/**
 * Built-in voices.
 *
 * Use `F1`–`F5` / `M1`–`M5` (what GET /voices lists). Each is a slot: the server resolves it to the
 * native speaker of the call's language in that slot, so `F1` in Spanish is a Spanish speaker and `F1`
 * in English an English one.
 *
 * `EN_*` pin the English speakers regardless of language. `ESCA_*` is the retired Spanish/Catalan pack
 * (its recordings measure as noisy in the top octave); do not use it in new code — `F1`–`M5` with
 * `language: 'es'` gives the vetted Spanish voices.
 *
 * This enum is a convenience, not the full set — a cloned voice's id (`clone_…`) is also a valid
 * `voice`, which is why the config fields accept any string.
 */
export enum VoiceStyle {
  // English reference speakers
  EN_F1 = "en_f1",
  EN_F2 = "en_f2",
  EN_F3 = "en_f3",
  EN_F4 = "en_f4",
  EN_F5 = "en_f5",
  EN_M1 = "en_m1",
  EN_M2 = "en_m2",
  EN_M3 = "en_m3",
  EN_M4 = "en_m4",
  EN_M5 = "en_m5",

  // Spanish / Catalan reference speakers
  /** @deprecated Retired pack; use F1 with language "es". */
  ESCA_F1 = "esca_f1",
  /** @deprecated Retired pack; use F2 with language "es". */
  ESCA_F2 = "esca_f2",
  /** @deprecated Retired pack; use F3 with language "es". */
  ESCA_F3 = "esca_f3",
  /** @deprecated Retired pack; use F4 with language "es". */
  ESCA_F4 = "esca_f4",
  /** @deprecated Retired pack; use F5 with language "es". */
  ESCA_F5 = "esca_f5",
  /** @deprecated Retired pack; use M1 with language "es". */
  ESCA_M1 = "esca_m1",
  /** @deprecated Retired pack; use M2 with language "es". */
  ESCA_M2 = "esca_m2",
  /** @deprecated Retired pack; use M3 with language "es". */
  ESCA_M3 = "esca_m3",
  /** @deprecated Retired pack; use M4 with language "es". */
  ESCA_M4 = "esca_m4",
  /** @deprecated Retired pack; use M5 with language "es". */
  ESCA_M5 = "esca_m5",

  F1 = "F1",
  F2 = "F2",
  F3 = "F3",
  F4 = "F4",
  F5 = "F5",
  M1 = "M1",
  M2 = "M2",
  M3 = "M3",
  M4 = "M4",
  M5 = "M5",
}

/**
 * A voice id: one of {@link VoiceStyle}, or any id the API serves — including a cloned voice's
 * `clone_…`. Typed to keep autocomplete on the enum while still accepting a runtime id.
 */
export type VoiceId = VoiceStyle | (string & {});

/**
 * Languages Lokutor supports.
 *
 * Nine, as of the Versa 2.0 rollout. The previous list carried 32 entries — Japanese, Chinese,
 * Arabic, Russian and more — that no shipped model was trained to speak, and omitted Catalan,
 * Galician and Basque, which it speaks well. Every one of these except English has a trained
 * language token in the model; English is its unmarked base case.
 *
 * `GET /languages` serves the live list; prefer it over hardcoding if you support user choice.
 */
export enum Language {
  ENGLISH = "en",
  SPANISH = "es",
  CATALAN = "ca",
  GALICIAN = "gl",
  BASQUE = "eu",
  PORTUGUESE = "pt",
  FRENCH = "fr",
  ITALIAN = "it",
  GERMAN = "de",
}

/**
 * Pass `'auto'` (or `''`) as a voice agent's language to let the agent follow the caller: the server
 * detects the caller's language, keeps it for short replies, and the agent answers and speaks in it.
 * Omit `language` for the default (`'en'`; with `agentId`, the agent's own).
 */
export const AUTO_LANGUAGE = 'auto';

/** A language for a voice agent: a {@link Language}, or `'auto'` / `''` to follow the caller. */
export type LanguageOption = Language | typeof AUTO_LANGUAGE | '';

/** The value the voice-agent protocol carries: `'auto'` and `''` are both `""`; anything else is unchanged. */
export function languageToWire(language: LanguageOption): Language | '' {
  return language === AUTO_LANGUAGE || language === '' ? '' : language;
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
  /** An API key, or a short-lived wst_ ticket from POST /ws-ticket. Optional when `auth` is given. */
  apiKey?: string;
  /**
   * Called before every connect (and reconnect) for the credential to use. In a browser, have it ask
   * your backend for a fresh ticket (POST https://api.lokutor.com/ws-ticket with your API key) so the
   * key itself never reaches the page; a ticket lasts 10 minutes, so reconnects need a new one.
   */
  auth?: () => string | Promise<string>;
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
  voice?: VoiceId;
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
  /** Custom vocabulary: names, brands and terms to write your way when what is heard is close (up to 100 of
   *  40 characters; needs `language`). https://docs.lokutor.com/guides/custom-vocabulary */
  keyterms?: string[];
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
  /** Custom vocabulary for this stream, applied to partial and final transcripts (up to 100 terms).
   *  https://docs.lokutor.com/guides/custom-vocabulary */
  keyterms?: string[];
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
  voice?: VoiceId;
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
  /** Pass to submitToolResult(call_id, result). */
  call_id: string;
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
  | 'agent.not_found'
  | 'billing.insufficient_balance'
  | 'billing.topup_required'
  | 'billing.limit_reached'
  | 'billing.account_disputed'
  | 'session.ended'
  | 'audio.microphone_unavailable'
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
    'session.ended',
    'audio.microphone_unavailable',
    'validation.invalid_voice',
    'validation.invalid_language',
    'validation.text_too_long',
    'validation.speed_out_of_range',
    'validation.steps_out_of_range',
    'validation.invalid_request_format',
    'internal.cancelled',
    'agent.not_found',
    'billing.insufficient_balance',
    'billing.topup_required',
    'billing.limit_reached',
    'billing.account_disputed',
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
