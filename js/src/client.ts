import {
  VoiceStyle,
  Language,
  DEFAULT_URLS,
  LokutorConfig,
  SynthesizeOptions
} from './types';

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
  private serverUrl: string;
  public prompt: string;
  public voice: VoiceStyle;
  public language: Language;

  // Callbacks
  private onTranscription?: (text: string) => void;
  private onResponse?: (text: string) => void;
  private onAudioCallback?: (data: Uint8Array) => void;
  private onStatus?: (status: string) => void;
  private onError?: (error: any) => void;

  private isConnected: boolean = false;

  constructor(config: LokutorConfig & {
    prompt: string,
    voice?: VoiceStyle,
    language?: Language
  }) {
    this.apiKey = config.apiKey;
    this.serverUrl = config.serverUrl || DEFAULT_URLS.VOICE_AGENT;
    this.prompt = config.prompt;
    this.voice = config.voice || VoiceStyle.F1;
    this.language = config.language || Language.ENGLISH;

    this.onTranscription = config.onTranscription;
    this.onResponse = config.onResponse;
    this.onAudioCallback = config.onAudio;
    this.onStatus = config.onStatus;
    this.onError = config.onError;
  }

  /**
   * Connect to the Lokutor Voice Agent server
   */
  public async connect(): Promise<boolean> {
    return new Promise((resolve, reject) => {
      try {
        let url = this.serverUrl;
        if (this.apiKey) {
          const separator = url.includes('?') ? '&' : '?';
          url += `${separator}api_key=${this.apiKey}`;
        }

        console.log(`🔗 Connecting to ${this.serverUrl}...`);

        this.ws = new WebSocket(url);
        this.ws.binaryType = 'arraybuffer';

        this.ws.onopen = () => {
          this.isConnected = true;
          console.log('✅ Connected to voice agent!');
          this.sendConfig();
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
          console.log('Disconnected');
        };

      } catch (err) {
        if (this.onError) this.onError(err);
        reject(err);
      }
    });
  }

  /**
   * Send initial configuration to the server
   */
  private sendConfig() {
    if (!this.ws || !this.isConnected) return;

    this.ws.send(JSON.stringify({ type: 'prompt', data: this.prompt }));
    this.ws.send(JSON.stringify({ type: 'voice', data: this.voice }));
    this.ws.send(JSON.stringify({ type: 'language', data: this.language }));

    console.log(`⚙️ Configured: voice=${this.voice}, language=${this.language}`);
  }

  /**
   * Send raw PCM audio data to the server
   * @param audioData Int16 PCM audio buffer
   */
  public sendAudio(audioData: Uint8Array) {
    if (this.ws && this.isConnected) {
      this.ws.send(audioData);
    }
  }

  /**
   * Handle incoming binary data (audio response)
   */
  private handleBinaryMessage(data: Uint8Array) {
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
            this.handleBinaryMessage(buffer);
          }
          break;
        case 'transcript':
          if (msg.role === 'user') {
            if (this.onTranscription) this.onTranscription(msg.data);
            console.log(`💬 You: ${msg.data}`);
          } else {
            if (this.onResponse) this.onResponse(msg.data);
            console.log(`🤖 Agent: ${msg.data}`);
          }
          break;
        case 'status':
          if (this.onStatus) this.onStatus(msg.data);
          const icons: Record<string, string> = {
            'interrupted': '⚡',
            'thinking': '🧠',
            'speaking': '🔊',
            'listening': '👂'
          };
          console.log(`${icons[msg.data] || ''} Status: ${msg.data}`);
          break;
        case 'error':
          if (this.onError) this.onError(msg.data);
          console.error(`❌ Server error: ${msg.data}`);
          break;
      }
    } catch (e) {
      // Not JSON or unknown format
    }
  }

  private audioListeners: ((data: Uint8Array) => void)[] = [];

  private emit(event: string, data: any) {
    if (event === 'audio') {
      if (this.onAudioCallback) this.onAudioCallback(data);
      this.audioListeners.forEach(l => l(data));
    }
  }

  public onAudio(callback: (data: Uint8Array) => void) {
    this.audioListeners.push(callback);
  }

  /**
   * Disconnect from the server
   */
  public disconnect() {
    if (this.ws) {
      this.ws.close();
      this.ws = null;
    }
  }
}

/**
 * Client for standalone Text-to-Speech synthesis
 */
export class TTSClient {
  private apiKey: string;
  private serverUrl: string;

  constructor(config: {
    apiKey: string;
    serverUrl?: string;
  }) {
    this.apiKey = config.apiKey;
    this.serverUrl = config.serverUrl || DEFAULT_URLS.TTS;
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
    onError?: (error: any) => void;
  }): Promise<void> {
    return new Promise((resolve, reject) => {
      try {
        let url = this.serverUrl;
        if (this.apiKey) {
          const separator = url.includes('?') ? '&' : '?';
          url += `${separator}api_key=${this.apiKey}`;
        }

        const ws = new WebSocket(url);
        ws.binaryType = 'arraybuffer';

        ws.onopen = () => {
          const req = {
            text: options.text,
            voice: options.voice || VoiceStyle.F1,
            lang: options.language || Language.ENGLISH,
            speed: options.speed || 1.05,
            steps: options.steps || 24,
            visemes: options.visemes || false
          };
          ws.send(JSON.stringify(req));
        };

        ws.onmessage = async (event) => {
          if (event.data instanceof ArrayBuffer) {
            if (options.onAudio) options.onAudio(new Uint8Array(event.data));
          } else {
            try {
              const msg = JSON.parse(event.data.toString());
              if (Array.isArray(msg) && options.onVisemes) {
                options.onVisemes(msg);
              }
            } catch (e) {
              // Ignore non-JSON or other messages
            }
          }
        };

        ws.onerror = (err) => {
          if (options.onError) options.onError(err);
          reject(err);
        };

        ws.onclose = () => {
          resolve();
        };

      } catch (err) {
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
