import {
  VoiceStyle,
  Language,
  DEFAULT_URLS,
  LokutorConfig,
  SynthesizeOptions,
  Viseme,
  ToolDefinition,
  ToolCall,
} from './types';
import { BrowserAudioManager } from './browser-audio';

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
  private onError?: (error: any) => void;

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

  constructor(config: LokutorConfig & {
    prompt: string,
    voice?: VoiceStyle,
    language?: Language,
    visemes?: boolean,
    onVisemes?: (visemes: Viseme[]) => void,
    enableAudio?: boolean,
    tools?: ToolDefinition[],
  }) {
    this.apiKey = config.apiKey;
    this.prompt = config.prompt;
    this.voice = config.voice || VoiceStyle.F1;
    this.language = config.language || Language.ENGLISH;

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
      try {
        let url = DEFAULT_URLS.VOICE_AGENT;
        if (this.apiKey) {
          const separator = url.includes('?') ? '&' : '?';
          url += `${separator}api_key=${this.apiKey}`;
        }

        console.log(`🔗 Connecting to ${DEFAULT_URLS.VOICE_AGENT}...`);

        this.ws = new WebSocket(url);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = async () => {
          this.isConnected = true;
          this.reconnectAttempts = 0;
          this.reconnecting = false;
          console.log('✅ Connected to voice agent!');
          this.sendConfig();

          if (this.audioManager) {
            await this.audioManager.startMicrophone((data) => {
              if (this.isConnected) {
                this.sendAudio(data);
              }
            });
          }

          resolve(true);
        };

        this.ws.onmessage = async (event) => {
          if (event.data instanceof ArrayBuffer) {
            this.handleBinaryMessage(new Uint8Array(event.data));
          } else {
            this.handleTextMessage(event.data.toString());
          }
        };

        this.ws.onerror = (err) => {
          console.error('❌ WebSocket error:', err);
          if (this.onError) this.onError(err);
          if (!this.isConnected) reject(err);
        };

        this.ws.onclose = () => {
          this.isConnected = false;
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
        if (this.onError) this.onError(err);
        reject(err);
      }
    });
  }

  /**
   * The "Golden Path" - Starts a managed session with hardware handled automatically.
   * This is the recommended way to start a conversation in both Browser and Node.js.
   */
  public async startManaged(config?: { audioManager?: AudioManager }): Promise<this> {
    this.enableAudio = true;
    if (config?.audioManager) {
      this.audioManager = config.audioManager;
    } else if (!this.audioManager) {
      if (typeof window !== 'undefined') {
        this.audioManager = new BrowserAudioManager();
      } else {
        try {
          // Node-specific managed playback
          const { NodeAudioManager } = await import('./node-audio.js');
          this.audioManager = new NodeAudioManager() as unknown as AudioManager;
        } catch (e) {
          console.error('❌ Failed to load NodeAudioManager. Please ensure "speaker" and "node-record-lpcm16" are installed.');
        }
      }
    }
    
    await this.connect();
    
    // Auto-start microphone if audio management is available
    if (this.audioManager && this.isConnected) {
      await this.audioManager.startMicrophone((data) => {
        this.sendAudio(data);
      });
    }
    
    return this;
  }

  /**
   * Send initial configuration to the server
   */
  private sendConfig() {
    if (!this.ws || !this.isConnected) return;

    this.ws.send(JSON.stringify({ type: 'prompt', data: this.prompt }));
    this.ws.send(JSON.stringify({ type: 'voice', data: this.voice }));
    this.ws.send(JSON.stringify({ type: 'language', data: this.language }));

    // Enable/disable viseme extraction on backend if requested
    this.ws.send(JSON.stringify({ type: 'visemes', data: this.wantVisemes }));

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
            timestamp: Date.now()
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
          if (Array.isArray(msg.data) && msg.data.length > 0) {
            this.emit('visemes', msg.data);
          }
          break;
        case 'error':
          if (this.onError) this.onError(msg.data);
          console.error(`❌ Server error: ${msg.data}`);
          break;
        case 'tool_call':
          console.log(`🛠️ Tool Call: ${msg.name}(${msg.arguments})`);
          break;
      }
    } catch (e) {
      // Not JSON or unknown format
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
   * Update the system prompt mid-conversation
   */
  public updatePrompt(newPrompt: string) {
    this.prompt = newPrompt;
    if (this.ws && this.isConnected) {
      try {
        this.ws.send(JSON.stringify({ type: 'prompt', data: newPrompt }));
        console.log(`⚙️ Updated prompt: ${newPrompt.substring(0, 50)}...`);
      } catch (error) {
        console.error('Error updating prompt:', error);
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
          startTime = Date.now();
        };

        ws.onmessage = async (event) => {
          refreshTimeout();
          if (event.data instanceof ArrayBuffer) {
            if (!firstByteReceived) {
              const ttfb = Date.now() - startTime;
              if (options.onTTFB) options.onTTFB(ttfb);
              firstByteReceived = true;
            }
            if (options.onAudio) options.onAudio(new Uint8Array(event.data));
          } else {
            try {
              const msg = JSON.parse(event.data.toString());
              if (Array.isArray(msg) && options.onVisemes) {
                options.onVisemes(msg);
              }
              // Check for manual EOS from server
              if (msg.type === 'eos') {
                if (activityTimeout) clearTimeout(activityTimeout);
                ws.close();
                resolve();
              }
            } catch (e) {
              // Ignore non-JSON or other messages
            }
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
