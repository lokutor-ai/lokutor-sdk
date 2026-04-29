import {
  VoiceStyle,
  Language,
  DEFAULT_URLS,
  LokutorConfig,
  SynthesizeOptions,
  Viseme,
  ToolDefinition,
  ToolCall,
  LokutorError,
  ErrorCode,
  isRetryable,
  VoiceInfo,
  LanguageInfo,
  ModelInfo,
  ServerConfig,
  ServerStatus,
  HealthStatus,
} from './types';
import { BrowserAudioManager } from './browser-audio';

function sdkTraceEnabled(): boolean {
  try {
    if (typeof window === 'undefined') return false;
    const w = window as any;
    return Boolean(w.LOKUTOR_TRACE) || window.localStorage?.getItem('lokutorTrace') === '1';
  } catch {
    return false;
  }
}

function sdkTrace(...args: any[]) {
  if (sdkTraceEnabled()) {
    console.log('[SDK TRACE]', ...args);
  }
}

function nowMs(): number {
  if (typeof performance !== 'undefined' && performance.now) {
    return performance.now();
  }
  return Date.now();
}

function wsToHttp(url: string): string {
  return url.replace(/^wss:/, 'https:').replace(/^ws:/, 'http:');
}

async function fetchJson<T>(url: string, timeoutMs = 10000): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const res = await fetch(url, {
      signal: controller.signal,
      headers: { Accept: 'application/json' },
    });
    clearTimeout(timer);
    if (!res.ok) {
      throw new LokutorError('internal.error', `HTTP ${res.status} from ${url}`, {
        detail: await res.text().catch(() => ''),
        retryable: res.status >= 500,
      });
    }
    return (await res.json()) as T;
  } catch (err) {
    clearTimeout(timer);
    if (err instanceof LokutorError) throw err;
    throw new LokutorError('internal.error', `Failed to fetch ${url}`, {
      original: err,
      retryable: true,
    });
  }
}

/**
 * Interface for audio hardware management (Browser/Node parity)
 */
export interface AudioManager {
  init(): Promise<void>;
  startMicrophone(onAudioInput: (pcm16Data: Uint8Array) => void): Promise<void>;
  stopMicrophone(): void;
  playAudio(pcm16Data: Uint8Array): void;
  stopPlayback(): void;
  cleanup(): void;
  isMicMuted(): boolean;
  setMuted(muted: boolean): void;
  getAmplitude(): number;
}

// Browser-compatible base64 to Uint8Array
function base64ToUint8Array(base64: string): Uint8Array {
  const binaryString = atob(base64);
  const bytes = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }
  return bytes;
}

function normalizeVisemes(payload: any): Viseme[] {
  if (!Array.isArray(payload)) return [];
  const normalized: Viseme[] = [];
  for (const item of payload) {
    if (!item || typeof item !== 'object') continue;
    const c = String(item.c ?? item.char ?? 'sil').toLowerCase();
    const t = Number(item.t ?? item.timestamp ?? 0);
    const v = Number(item.v ?? item.id ?? 0);
    normalized.push({
      v: Number.isFinite(v) ? v : 0,
      c,
      t: Number.isFinite(t) ? t : 0,
    });
  }
  return normalized;
}

function extractVisemePayload(msg: any): Viseme[] {
  if (Array.isArray(msg?.data)) {
    return normalizeVisemes(msg.data);
  }

  if (Array.isArray(msg?.data?.visemes)) {
    return normalizeVisemes(msg.data.visemes);
  }

  if (msg?.data && !Array.isArray(msg.data) && typeof msg.data === 'object') {
    const singularInData = normalizeVisemes([msg.data]);
    if (singularInData.length > 0) return singularInData;
  }

  if (msg && !Array.isArray(msg) && typeof msg === 'object') {
    const singularAtRoot = normalizeVisemes([msg]);
    if (singularAtRoot.length > 0) return singularAtRoot;
  }

  return [];
}

/**
 * Main client for Lokutor Voice Agent SDK
 * 
 * Provides a high-level interface for real-time voice conversations.
 */
export class VoiceAgentClient {
  private ws: WebSocket | null = null;
  private apiKey: string;
  public prompt: string;
  public voice: VoiceStyle;
  public language: Language;
  public tools: ToolDefinition[] = [];

  // Callbacks
  private onTranscription?: (text: string) => void;
  private onResponse?: (text: string) => void;
  private onAudioCallback?: (data: Uint8Array) => void;
  private onVisemesCallback?: (visemes: Viseme[]) => void;
  private onStatus?: (status: string) => void;
  private onError?: (error: LokutorError) => void;

  private isConnected: boolean = false;
  private messages: Array<{ role: 'user' | 'agent'; text: string; timestamp: number }> = [];
  private visemeListeners: ((visemes: Viseme[]) => void)[] = [];
  private wantVisemes: boolean = false;

  private audioManager: AudioManager | null = null;
  private enableAudio: boolean = false;
  private currentGeneration: number = 0;
  private listeners: Record<string, Function[]> = {};

  // Connection resilience
  private isUserDisconnect: boolean = false;
  private reconnecting: boolean = false;
  private reconnectAttempts: number = 0;
  private maxReconnectAttempts: number = 5;

  private serverUrl: string;

  constructor(config: LokutorConfig & {
    prompt: string,
    voice?: VoiceStyle,
    language?: Language,
    visemes?: boolean,
    onVisemes?: (visemes: Viseme[]) => void,
    enableAudio?: boolean,
    tools?: ToolDefinition[],
    serverUrl?: string,
  }) {
    this.apiKey = config.apiKey;
    this.prompt = config.prompt;
    this.voice = config.voice || VoiceStyle.F1;
    this.language = config.language || Language.ENGLISH;
    this.serverUrl = config.serverUrl || DEFAULT_URLS.VOICE_AGENT;

    this.onTranscription = config.onTranscription;
    this.onResponse = config.onResponse;
    this.onAudioCallback = config.onAudio;
    this.onVisemesCallback = config.onVisemes;
    this.onStatus = config.onStatus;
    this.onError = config.onError;
    this.wantVisemes = config.visemes || false;
    this.enableAudio = config.enableAudio ?? false;
    this.tools = config.tools || [];
  }

  /**
   * Connect to the Lokutor Voice Agent server
   * @param customAudioManager Optional replacement for the default audio hardware handler
   */
  public async connect(customAudioManager?: AudioManager): Promise<boolean> {
    this.isUserDisconnect = false;

    if (this.enableAudio || customAudioManager) {
      if (customAudioManager) {
        this.audioManager = customAudioManager;
      } else if (!this.audioManager && typeof window !== 'undefined') {
        this.audioManager = new BrowserAudioManager();
      }

      if (this.audioManager) {
        await this.audioManager.init();
      }
    }

    return new Promise((resolve, reject) => {
      let settled = false;
      const settle = (fn: () => void) => {
        if (!settled) {
          settled = true;
          fn();
        }
      };

      try {
        let url = this.serverUrl;
        if (this.apiKey) {
          const separator = url.includes('?') ? '&' : '?';
          url += `${separator}api_key=${this.apiKey}`;
        }

        const redactedUrl = url.replace(/api_key=[^&]+/, 'api_key=***');
        sdkTrace('ws.connect', {
          endpoint: this.serverUrl,
          url: redactedUrl,
          enableAudio: this.enableAudio,
          wantVisemes: this.wantVisemes,
          hasAudioManager: Boolean(this.audioManager)
        });

        console.log(`🔗 Connecting to ${this.serverUrl}...`);

        this.ws = new WebSocket(url);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = async () => {
          this.isConnected = true;
          this.reconnectAttempts = 0;
          this.reconnecting = false;
          console.log('✅ Connected to voice agent!');
          sdkTrace('ws.open');
          this.sendConfig();

          if (this.audioManager) {
            await this.audioManager.startMicrophone((data) => {
              if (this.isConnected) {
                this.sendAudio(data);
              }
            });
          }

          settle(() => resolve(true));
        };

        this.ws.onmessage = async (event) => {
          if (event.data instanceof ArrayBuffer) {
            sdkTrace('ws.message.binary', { bytes: event.data.byteLength });
            this.handleBinaryMessage(new Uint8Array(event.data));
          } else {
            sdkTrace('ws.message.text', { length: String(event.data).length });
            this.handleTextMessage(event.data.toString());
          }
        };

        this.ws.onerror = (err) => {
          const error = new LokutorError('ws.close', 'WebSocket connection error', {
            detail: `readyState=${this.ws?.readyState}, bufferedAmount=${this.ws?.bufferedAmount}`,
            original: err,
            retryable: true,
          });
          console.error('❌ WebSocket error:', error.message);
          sdkTrace('ws.error', { code: error.code, message: error.message });
          if (this.onError) this.onError(error);
          if (!this.isConnected) {
            settle(() => reject(error));
          }
        };

        this.ws.onclose = (event) => {
          this.isConnected = false;
          const diagnostic = {
            code: event.code,
            reason: event.reason,
            wasClean: event.wasClean,
            url: this.serverUrl,
            isUserDisconnect: this.isUserDisconnect,
            reconnectAttempts: this.reconnectAttempts
          };
          sdkTrace('ws.close', diagnostic);

          if (!settled && !this.isUserDisconnect) {
            const error = new LokutorError('ws.close', `WebSocket closed unexpectedly (code ${event.code})`, {
              detail: event.reason || 'No reason provided',
              retryable: event.code !== 1008,
            });
            settle(() => reject(error));
            return;
          }

          if (!event.wasClean && event.code === 1006 && this.reconnectAttempts === 0 && !this.isUserDisconnect) {
            console.error('❌ Connection rejected (code 1006). Likely causes: invalid API key, endpoint unavailable, or CORS blocked.');
            console.error('   URL:', this.serverUrl.replace(/api_key=[^&]+/, 'api_key=***'));
          }

          console.log(`🔌 WebSocket closed — code: ${event.code}, reason: "${event.reason || 'none'}", clean: ${event.wasClean}`);

          if (!this.isUserDisconnect && this.reconnectAttempts < this.maxReconnectAttempts) {
            this.reconnecting = true;
            this.reconnectAttempts++;
            const backoffDelay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 10000);

            console.warn(`Connection lost. Reconnecting in ${backoffDelay}ms (attempt ${this.reconnectAttempts}/${this.maxReconnectAttempts})`);

            if (this.onStatus) this.onStatus('reconnecting');

            setTimeout(() => {
              this.connect().catch(e => console.error("Reconnect failed", e));
            }, backoffDelay);
          } else {
            console.log('Disconnected');
            if (this.onStatus) this.onStatus('disconnected');
          }
        };

      } catch (err) {
        const error = err instanceof LokutorError ? err : new LokutorError('internal.error', 'Failed to create WebSocket connection', { original: err });
        if (this.onError) this.onError(error);
        settle(() => reject(error));
      }
    });
  }

  /**
   * The "Golden Path" - Starts a managed session with hardware handled automatically.
   * This is the recommended way to start a conversation in browser environments.
   */
  public async startManaged(config?: { audioManager?: AudioManager }): Promise<this> {
    this.enableAudio = true;
    if (config?.audioManager) {
      this.audioManager = config.audioManager;
    } else if (!this.audioManager) {
      if (typeof window === 'undefined') {
        throw new LokutorError('internal.error', 'startManaged() requires a browser environment. Pass a custom audioManager for non-browser runtimes.', { retryable: false });
      }
      this.audioManager = new BrowserAudioManager();
    }

    await this.connect();

    return this;
  }

  /**
   * Send initial configuration to the server
   */
  private sendConfig() {
    if (!this.ws || !this.isConnected) return;

    // Send feature/config flags first so the first generated response uses them.
    this.ws.send(JSON.stringify({ type: 'visemes', data: this.wantVisemes }));
    this.ws.send(JSON.stringify({ type: 'voice', data: this.voice }));
    this.ws.send(JSON.stringify({ type: 'language', data: this.language }));
    this.ws.send(JSON.stringify({ type: 'prompt', data: this.prompt }));

    sdkTrace('ws.send.config', {
      promptLen: this.prompt?.length || 0,
      voice: this.voice,
      language: this.language,
      visemes: this.wantVisemes,
      tools: this.tools?.length || 0
    });

    if (this.tools && this.tools.length > 0) {
      this.ws.send(JSON.stringify({ type: 'tools', data: this.tools }));
    }

    console.log(`⚙️ Configured: voice=${this.voice}, language=${this.language}, visemes=${this.wantVisemes}, tools=${this.tools.length}`);
  }

  /**
   * Send raw PCM audio data to the server
   * @param audioData Int16 PCM audio buffer
   */
  public sendAudio(audioData: Uint8Array) {
    if (this.ws && this.ws.readyState === WebSocket.OPEN && this.isConnected) {
      this.ws.send(audioData);
    }
  }

  /**
   * Handle incoming binary data (audio response)
   */
  private handleBinaryMessage(data: Uint8Array, generation?: number) {
    if (generation !== undefined && generation < this.currentGeneration) {
      console.log(`🗑️ Discarding ghost audio (Gen ${generation} < ${this.currentGeneration})`);
      return;
    }
    if (this.audioManager) {
      this.audioManager.playAudio(data);
    }
    this.emit('audio', data);
  }

  /**
   * Handle incoming text messages (metadata/transcriptions)
   */
  private handleTextMessage(text: string) {
    try {
      const msg = JSON.parse(text);
      if (!msg || typeof msg !== 'object') {
        return;
      }
      sdkTrace('ws.recv.type', {
        type: msg.type,
        hasData: Object.prototype.hasOwnProperty.call(msg, 'data'),
        dataKind: Array.isArray(msg.data) ? 'array' : typeof msg.data,
        generation: msg.generation ?? null
      });
      switch (msg.type) {
        case 'audio':
          if (msg.data) {
            const buffer = base64ToUint8Array(msg.data);
            this.handleBinaryMessage(buffer, msg.generation);
          }
          break;
        case 'transcript':
          const role = msg.role === 'user' ? 'user' : 'agent';
          // Store in history
          this.messages.push({
            role,
            text: msg.data,
            timestamp: nowMs()
          });

          if (msg.role === 'user') {
            if (this.onTranscription) this.onTranscription(msg.data);
            console.log(`💬 You: ${msg.data}`);
          } else {
            if (this.onResponse) this.onResponse(msg.data);
            console.log(`🤖 Agent: ${msg.data}`);
          }
          break;
        case 'status':
          if (msg.data === 'thinking') {
            const newGen = msg.generation || 0;
            if (newGen > this.currentGeneration) {
              console.log(`🧠 New thought (Gen ${newGen}) - Clearing audio queue`);
              this.currentGeneration = newGen;
              if (this.audioManager) this.audioManager.stopPlayback();
            }
          }
          if (msg.data === 'interrupted' && this.audioManager) {
            this.audioManager.stopPlayback();
          }
          if (this.onStatus) this.onStatus(msg.data);
          const icons: Record<string, string> = {
            'interrupted': '⚡',
            'thinking': '🧠',
            'speaking': '🔊',
            'listening': '👂'
          };
          console.log(`${icons[msg.data] || ''} Status: ${msg.data}`);
          break;
        case 'visemes':
        case 'viseme': {
          const msgGen = msg.generation ?? this.currentGeneration;
          if (msgGen < this.currentGeneration) {
            sdkTrace('visemes.discard', { msgGen, currentGen: this.currentGeneration });
            break;
          }
          const normalized = extractVisemePayload(msg);
          const explicitlyEmptyArray = Array.isArray(msg?.data) || Array.isArray(msg?.data?.visemes);
          sdkTrace('visemes.recv', {
            rawType: msg.type,
            normalizedCount: normalized.length,
            first: normalized[0] ?? null
          });
          if (normalized.length > 0 || explicitlyEmptyArray) {
            this.emit('visemes', normalized);
          }
          break;
        }
        case 'error': {
          const backendCode = msg.data?.code ?? 'internal.error';
          const backendMessage = msg.data?.message ?? msg.data ?? 'Unknown server error';
          const backendDetail = msg.data?.detail;
          const backendRetryable = msg.data?.retryable ?? true;
          const error = new LokutorError(backendCode as ErrorCode, backendMessage, {
            detail: backendDetail,
            retryable: backendRetryable,
          });
          if (this.onError) this.onError(error);
          console.error(`❌ Server error: [${error.code}] ${error.message}`);
          break;
        }
        case 'tool_call':
          console.log(`🛠️ Tool Call: ${msg.name}(${msg.arguments})`);
          break;
      }
    } catch (e) {
      sdkTrace('ws.recv.parse_error', { preview: text?.slice(0, 120) });
      console.debug('Failed to parse message:', e);
    }
  }

  /**
   * Register an event listener (for Python parity)
   */
  public on(event: string, callback: Function): this {
    if (!this.listeners[event]) {
      this.listeners[event] = [];
    }
    this.listeners[event].push(callback);
    return this;
  }

  /**
   * Internal emitter for all events
   */
  private emit(event: string, ...args: any[]) {
    // Legacy property-style callbacks
    const legacyMap: Record<string, string> = {
      'transcription': 'onTranscription',
      'response': 'onResponse',
      'audio': 'onAudioCallback',
      'visemes': 'onVisemesCallback',
      'status': 'onStatus',
      'error': 'onError',
    };

    const legacyKey = legacyMap[event];
    if (legacyKey && (this as any)[legacyKey]) {
      try {
        (this as any)[legacyKey](...args);
      } catch (e) {
        console.error(`Error in legacy callback ${legacyKey}:`, e);
      }
    }

    // New style listeners
    if (this.listeners[event]) {
      this.listeners[event].forEach(cb => {
        try {
          cb(...args);
        } catch (e) {
          console.error(`Error in listener for ${event}:`, e);
        }
      });
    }
  }

  public onAudio(callback: (data: Uint8Array) => void) {
    this.on('audio', callback);
  }

  public onVisemes(callback: (visemes: Viseme[]) => void) {
    this.on('visemes', callback);
  }

  /**
   * Disconnect from the server
   */
  public disconnect() {
    this.isUserDisconnect = true;
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
    if (this.audioManager) {
      this.audioManager.cleanup();
    }
    this.isConnected = false;
    this.reconnecting = false;
    this.reconnectAttempts = 0;
  }

  /**
   * Returns true if the client is currently connected.
   */
  public get connected(): boolean {
    return this.isConnected;
  }

  /**
   * Returns the current generation counter.
   * Useful for correlating audio/viseme chunks with utterances.
   */
  public get generation(): number {
    return this.currentGeneration;
  }

  /**
   * Toggles the microphone mute state (if managed by client)
   * returns the new mute state
   */
  public toggleMute(): boolean {
    if (this.audioManager) {
      const isMuted = this.audioManager.isMicMuted();
      this.audioManager.setMuted(!isMuted);
      return !isMuted;
    }
    return false;
  }

  /**
   * Gets the microphone volume amplitude 0-1 (if managed by client)
   */
  public getAmplitude(): number {
    if (this.audioManager) {
      return this.audioManager.getAmplitude();
    }
    return 0;
  }

  /**
   * Fetch available voice styles from the server.
   * No authentication required.
   */
  static async fetchVoices(baseUrl?: string): Promise<VoiceInfo[]> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    const data = await fetchJson<{ voices: VoiceInfo[] }>(`${url}/voices`);
    return data.voices || [];
  }

  /**
   * Fetch supported languages from the server.
   * No authentication required.
   */
  static async fetchLanguages(baseUrl?: string): Promise<LanguageInfo[]> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    const data = await fetchJson<{ languages: LanguageInfo[] }>(`${url}/languages`);
    return data.languages || [];
  }

  /**
   * Fetch loaded TTS model versions from the server.
   * No authentication required.
   */
  static async fetchModels(baseUrl?: string): Promise<ModelInfo[]> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    const data = await fetchJson<{ models: ModelInfo[] }>(`${url}/models`);
    return data.models || [];
  }

  /**
   * Fetch server configuration (limits and defaults).
   * No authentication required.
   */
  static async fetchConfig(baseUrl?: string): Promise<ServerConfig> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    return fetchJson<ServerConfig>(`${url}/config`);
  }

  /**
   * Fetch rich runtime status from the server.
   * No authentication required.
   */
  static async fetchStatus(baseUrl?: string): Promise<ServerStatus> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    return fetchJson<ServerStatus>(`${url}/status`);
  }

  /**
   * Fetch health/liveness status from the server.
   * No authentication required.
   */
  static async fetchHealth(baseUrl?: string): Promise<HealthStatus> {
    const url = wsToHttp(baseUrl || DEFAULT_URLS.VOICE_AGENT);
    return fetchJson<HealthStatus>(`${url}/health`);
  }

  /**
   * Update the system prompt mid-conversation
   */
  public updatePrompt(newPrompt: string) {
    this.prompt = newPrompt;
    if (this.ws && this.ws.readyState === WebSocket.OPEN && this.isConnected) {
      try {
        this.ws.send(JSON.stringify({ type: 'prompt', data: newPrompt }));
        console.log(`⚙️ Updated prompt: ${newPrompt.substring(0, 50)}...`);
      } catch (error) {
        const err = new LokutorError('internal.error', 'Failed to update prompt', { original: error });
        if (this.onError) this.onError(err);
        console.error('Error updating prompt:', err.message);
      }
    } else {
      console.warn('Not connected - prompt will be updated on next connection');
    }
  }

  /**
   * Get full conversation transcript
   */
  public getTranscript(): Array<{ role: 'user' | 'agent'; text: string; timestamp: number }> {
    return this.messages.slice();
  }

  /**
   * Get conversation as formatted text
   */
  public getTranscriptText(): string {
    return this.messages
      .map(msg => `${msg.role === 'user' ? 'You' : 'Agent'}: ${msg.text}`)
      .join('\n');
  }
}

/**
 * Client for standalone Text-to-Speech synthesis
 */
export class TTSClient {
  private apiKey: string;

  constructor(config: {
    apiKey: string;
  }) {
    this.apiKey = config.apiKey;
  }

  /**
   * Synthesize text to speech
   * 
   * This opens a temporary WebSocket connection, sends the request, 
   * and streams back the audio.
   */
  public synthesize(options: {
    text: string;
    voice?: VoiceStyle;
    language?: Language;
    speed?: number;
    steps?: number;
    visemes?: boolean;
    onAudio?: (data: Uint8Array) => void;
    onVisemes?: (visemes: any[]) => void;
    onTTFB?: (ms: number) => void;
    onError?: (error: any) => void;
  }): Promise<void> {
    return new Promise((resolve, reject) => {
      let activityTimeout: any;
      let ws: WebSocket;
      let startTime: number;
      let firstByteReceived = false;

      const refreshTimeout = () => {
        if (activityTimeout) clearTimeout(activityTimeout);
        activityTimeout = setTimeout(() => {
          console.log("⏱️ TTS synthesis reached inactivity timeout (2s) - resolving");
          if (ws) ws.close();
          resolve();
        }, 2000);
      };

      try {
        let url = DEFAULT_URLS.TTS;
        if (this.apiKey) {
          const separator = url.includes('?') ? '&' : '?';
          url += `${separator}api_key=${this.apiKey}`;
        }

        ws = new WebSocket(url);
        ws.binaryType = 'arraybuffer';

        ws.onopen = () => {
          refreshTimeout();
          const req = {
            text: options.text,
            voice: options.voice || VoiceStyle.F1,
            lang: options.language || Language.ENGLISH,
            speed: options.speed || 1.05,
            steps: options.steps || 24,
            visemes: options.visemes || false
          };
          ws.send(JSON.stringify(req));
          startTime = nowMs();
        };

        ws.onmessage = async (event) => {
          refreshTimeout();
          if (event.data instanceof ArrayBuffer) {
            if (!firstByteReceived) {
              const ttfb = nowMs() - startTime;
              if (options.onTTFB) options.onTTFB(ttfb);
              firstByteReceived = true;
            }
            if (options.onAudio) options.onAudio(new Uint8Array(event.data));
            return;
          }
          const text = event.data.toString();
          if (text === 'EOS') {
            if (activityTimeout) clearTimeout(activityTimeout);
            ws.close();
            resolve();
            return;
          }

          try {
            const msg = JSON.parse(text);
            
            if (msg.type === 'audio' && msg.data) {
              const audioBuffer = base64ToUint8Array(msg.data);
              if (!firstByteReceived) {
                const ttfb = nowMs() - startTime;
                if (options.onTTFB) options.onTTFB(ttfb);
                firstByteReceived = true;
              }
              if (options.onAudio) options.onAudio(audioBuffer);
              return;
            }

            if (msg.type === 'visemes' && Array.isArray(msg.data) && options.onVisemes) {
              options.onVisemes(normalizeVisemes(msg.data));
              return;
            }

            if (Array.isArray(msg) && options.onVisemes) {
              options.onVisemes(normalizeVisemes(msg));
              return;
            }
            
            if (msg.type === 'eos') {
              if (activityTimeout) clearTimeout(activityTimeout);
              ws.close();
              resolve();
            }
          } catch (e) {
          }
        };

        ws.onerror = (err) => {
          if (activityTimeout) clearTimeout(activityTimeout);
          if (options.onError) options.onError(err);
          reject(err);
        };

        ws.onclose = () => {
          if (activityTimeout) clearTimeout(activityTimeout);
          resolve();
        };

      } catch (err) {
        if (activityTimeout) clearTimeout(activityTimeout);
        if (options.onError) options.onError(err);
        reject(err);
      }
    });
  }
}

/**
 * Quick function to start a conversation (requires manual audio piping in JS)
 */
export async function simpleConversation(config: LokutorConfig & { prompt: string }) {
  const client = new VoiceAgentClient(config);
  await client.connect();
  return client;
}

/**
 * Quick function for standalone TTS synthesis
 */
export async function simpleTTS(options: SynthesizeOptions & { apiKey: string, onAudio: (buf: Uint8Array) => void }) {
  const client = new TTSClient({ apiKey: options.apiKey });
  return client.synthesize(options);
}
