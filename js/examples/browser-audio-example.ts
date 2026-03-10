/**
 * Modern example using the new BrowserAudioManager for seamless voice conversations
 * This demonstrates how to use the SDK's built-in audio utilities instead of 
 * implementing everything manually.
 */

import {
  VoiceAgentClient,
  BrowserAudioManager,
  VoiceStyle,
  Language,
  AUDIO_CONFIG,
  VoiceAgentOptions,
  Viseme,
} from '@lokutor/sdk';

interface ConvoOptions extends VoiceAgentOptions {
  prompt?: string;
  voice?: VoiceStyle;
  language?: Language;
  serverUrl?: string;
  visemes?: boolean;
  onTranscription?: (text: string, isUser: boolean) => void;
  onVisemes?: (visemes: Viseme[]) => void;
  onStatusChange?: (status: string) => void;
  onError?: (err: any) => void;
}

const IS_LOCAL = import.meta.env.VITE_IS_LOCAL === 'true';
const LOKUTOR_WS_URL = IS_LOCAL
  ? 'ws://localhost:8080/ws/agent'
  : 'wss://api.lokutor.com/ws/agent';

/**
 * Simplified conversation agent using SDK audio utilities
 */
export class ConvoAgent {
  private client: VoiceAgentClient | null = null;
  private audioManager: BrowserAudioManager | null = null;
  private isConnected: boolean = false;
  private options: ConvoOptions = {};
  private timerInterval: any = null;
  private wantVisemes: boolean = false;

  constructor(options: ConvoOptions = {}) {
    this.options = options;
    this.wantVisemes = options.visemes ?? false;
  }

  async init() {
    // Initialize audio manager with browser audio features
    this.audioManager = new BrowserAudioManager({
      inputSampleRate: AUDIO_CONFIG.SAMPLE_RATE,
      outputSampleRate: AUDIO_CONFIG.SPEAKER_SAMPLE_RATE,
      autoGainControl: true,
      echoCancellation: true,
      noiseSuppression: true,
      onInputError: (err) => {
        console.error('❌ Microphone error:', err);
        if (this.options.onError) this.options.onError(err);
      },
    });

    await this.audioManager.init({
      enabled: true,
      fftSize: 256,
    });

    console.log('✅ Audio manager initialized');
  }

  async connect() {
    console.log('--- ConvoAgent connecting... ---');
    await this.init();

    const config = {
      apiKey: '',
      serverUrl: LOKUTOR_WS_URL,
      prompt: this.options.prompt || 'You are a helpful and friendly AI assistant.',
      voice: this.options.voice || VoiceStyle.M1,
      language: this.options.language,
      
      // Use SDK's audio manager for playback
      onAudio: (buffer: Uint8Array) => {
        if (Math.random() < 0.05) {
          console.log(`🔊 Received audio chunk: ${buffer.byteLength} bytes`);
        }
        // Simplified playback using SDK - no manual Int16Array conversion needed
        this.audioManager?.playAudio(buffer);
      },
      
      onTranscription: (text: string) => {
        console.log('📝 Transcription:', text);
        if (this.options.onTranscription) {
          this.options.onTranscription(text, true);
        }
      },
      
      onResponse: (text: string) => {
        console.log('🤖 AI Response:', text);
        if (this.options.onTranscription) {
          this.options.onTranscription(text, false);
        }
      },
      
      onStatus: (status: string) => {
        console.log('📡 Status Change:', status);
        if (this.options.onStatusChange) {
          this.options.onStatusChange(status);
        }
        if (status === 'interrupted') {
          this.audioManager?.stopPlayback();
        }
      },
      
      onError: (err: any) => {
        console.error('❌ SDK Error:', err);
        if (this.options.onError) {
          this.options.onError(err);
        }
      },
    };

    console.log('🚀 Initializing VoiceAgentClient with config:', {
      voice: config.voice,
      language: config.language || 'Any',
      server: config.serverUrl,
    });

    this.client = new VoiceAgentClient(config);

    const connected = await this.client.connect();
    console.log('🔗 Connection Result:', connected);

    if (connected) {
      this.isConnected = true;
      // Start microphone (handles resampling and PCM conversion internally)
      await this.startMicrophone();
      this.startTimer();
    }

    return connected;
  }

  private async startMicrophone() {
    if (!this.audioManager) return;

    try {
      // SDK handles all audio processing internally:
      // - Resampling from hardware rate to 44.1kHz
      // - PCM conversion Float32 to Int16
      // - Echo prevention
      await this.audioManager.startMicrophone((pcm16Data: Uint8Array) => {
        if (!this.isConnected || !this.client) return;

        if (Math.random() < 0.05) {
          console.log(`📤 Sending audio: ${pcm16Data.byteLength} bytes`);
        }

        this.client?.sendAudio(pcm16Data);
      });

      console.log('🎤 Microphone started with SDK audio manager');
    } catch (err) {
      console.error('Error accessing microphone:', err);
      if (this.options.onError) {
        this.options.onError(err);
      }
    }
  }

  private startTimer() {
    // Optional: implement conversation timeout
    // this.timerInterval = window.setInterval(() => {
    //   const elapsed = Date.now() - this.startTime;
    //   if (elapsed >= 60000) { // 1 minute limit
    //     this.disconnect();
    //     if (this.options.onStatusChange) {
    //       this.options.onStatusChange('limit_reached');
    //     }
    //   }
    // }, 1000);
  }

  disconnect() {
    this.isConnected = false;

    if (this.client) {
      this.client.disconnect();
      this.client = null;
    }

    if (this.audioManager) {
      this.audioManager.stopMicrophone();
      this.audioManager.stopPlayback();
      this.audioManager.cleanup();
      this.audioManager = null;
    }

    if (this.timerInterval) {
      clearInterval(this.timerInterval);
      this.timerInterval = null;
    }

    console.log('Disconnected');
  }

  toggleMute(): boolean {
    if (!this.audioManager) return false;

    const isMuted = this.audioManager.isMicMuted();
    this.audioManager.setMuted(!isMuted);
    console.log(isMuted ? '🔊 Unmuted' : '🔇 Muted');
    return !isMuted;
  }

  /**
   * Send text message with optional viseme request
   */
  async sendText(text: string, requestVisemes?: boolean): Promise<void> {
    if (!this.client) {
      throw new Error('Not connected');
    }

    const wantVisemes = requestVisemes ?? this.wantVisemes;
    const message = {
      type: 'text',
      data: text,
      visemes: wantVisemes,
    };

    // Access the underlying WebSocket to send custom message
    // This requires extending VoiceAgentClient or accessing it directly
    // For now, we'll use client.sendAudio as a workaround and rely on
    // the server to handle the text message type  
    if ((this.client as any).ws) {
      (this.client as any).ws.send(JSON.stringify(message));
    }
  }

  /**
   * Enable/disable viseme requests for text synthesis
   */
  setVisemesEnabled(enabled: boolean): void {
    this.wantVisemes = enabled;
    console.log(enabled ? '👁️  Visemes enabled' : '👁️  Visemes disabled');
  }

  /**
   * Get current audio amplitude for visualization (0-1)
   */
  getAmplitude(): number {
    return this.audioManager?.getAmplitude() ?? 0;
  }

  /**
   * Get waveform data for visualization
   */
  getWaveformData(): Uint8Array {
    return this.audioManager?.getWaveformData() ?? new Uint8Array(0);
  }

  /**
   * Get frequency data for visualization
   */
  getFrequencyData(): Uint8Array {
    return this.audioManager?.getFrequencyData() ?? new Uint8Array(0);
  }

  isRecording(): boolean {
    return this.audioManager?.isRecording() ?? false;
  }

  isConnectedToServer(): boolean {
    return this.isConnected;
  }
}
