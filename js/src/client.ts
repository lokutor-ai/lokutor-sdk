import { WebSocket } from 'ws';
import { 
  VoiceStyle, 
  Language, 
  DEFAULT_URLS, 
  LokutorConfig,
  SynthesizeOptions
} from './types';

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
  private onAudioCallback?: (data: Buffer) => void;
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
        console.log(`🔗 Connecting to ${this.serverUrl}...`);

        const headers: Record<string, string> = {};
        if (this.apiKey) {
          headers['X-API-Key'] = this.apiKey;
        }

        this.ws = new WebSocket(this.serverUrl, {
          headers: headers
        });

        this.ws.on('open', () => {
          this.isConnected = true;
          console.log('✅ Connected to voice agent!');
          this.sendConfig();
          resolve(true);
        });

        this.ws.on('message', (data, isBinary) => {
          if (isBinary) {
            this.handleBinaryMessage(data as Buffer);
          } else {
            this.handleTextMessage(data.toString());
          }
        });

        this.ws.on('error', (err) => {
          console.error('❌ WebSocket error:', err);
          if (this.onError) this.onError(err);
          if (!this.isConnected) reject(err);
        });

        this.ws.on('close', () => {
          this.isConnected = false;
          console.log('Disconnected');
        });

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
  public sendAudio(audioData: Buffer | Uint8Array) {
    if (this.ws && this.isConnected) {
      this.ws.send(audioData, { binary: true });
    }
  }

  /**
   * Handle incoming binary data (audio response)
   */
  private handleBinaryMessage(data: Buffer) {
    // This should be handled by the audio output system
    // We emit an event or call a callback if configured
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
            const buffer = Buffer.from(msg.data, 'base64');
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

  private audioListeners: ((data: Buffer) => void)[] = [];

  private emit(event: string, data: any) {
    if (event === 'audio') {
      if (this.onAudioCallback) this.onAudioCallback(data);
      this.audioListeners.forEach(l => l(data));
    }
  }

  public onAudio(callback: (data: Buffer) => void) {
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
  private onAudioCallback?: (data: Buffer) => void;
  private onVisemesCallback?: (visemes: any[]) => void;
  private onErrorCallback?: (error: any) => void;

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
    onAudio?: (data: Buffer) => void;
    onVisemes?: (visemes: any[]) => void;
    onError?: (error: any) => void;
  }): Promise<void> {
    return new Promise((resolve, reject) => {
      try {
        const headers: Record<string, string> = {};
        if (this.apiKey) {
          headers['X-API-Key'] = this.apiKey;
        }

        const ws = new WebSocket(this.serverUrl, { headers });

        ws.on('open', () => {
          const req = {
            text: options.text,
            voice: options.voice || VoiceStyle.F1,
            lang: options.language || Language.ENGLISH,
            speed: options.speed || 1.05,
            steps: options.steps || 24,
            visemes: options.visemes || false
          };
          ws.send(JSON.stringify(req));
        });

        ws.on('message', (data, isBinary) => {
          if (isBinary) {
            if (options.onAudio) options.onAudio(data as Buffer);
          } else {
            try {
              const msg = JSON.parse(data.toString());
              if (Array.isArray(msg) && options.onVisemes) {
                options.onVisemes(msg);
              }
            } catch (e) {
              // Ignore non-JSON or other messages
            }
          }
        });

        ws.on('error', (err) => {
          if (options.onError) options.onError(err);
          reject(err);
        });

        ws.on('close', () => {
          resolve();
        });

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
export async function simpleTTS(options: SynthesizeOptions & { apiKey: string, onAudio: (buf: Buffer) => void }) {
  const client = new TTSClient({ apiKey: options.apiKey });
  return client.synthesize(options);
}
