import { AUDIO_CONFIG } from './types';
import { float32ToPcm16, pcm16ToFloat32, StreamResampler, calculateRMS } from './audio-utils';

/**
 * Configuration for browser audio handling
 */
export interface BrowserAudioConfig {
  inputSampleRate?: number;
  outputSampleRate?: number;
  autoGainControl?: boolean;
  echoCancellation?: boolean;
  noiseSuppression?: boolean;
  onInputError?: (error: Error) => void;
}

/**
 * Analyser configuration for audio visualization
 */
export interface AnalyserConfig {
  enabled?: boolean;
  fftSize?: number;
}

/**
 * Browser-based audio manager for Web Audio API operations
 * Handles microphone input, speaker output, and visualization
 */
export class BrowserAudioManager {
  private audioContext: AudioContext | null = null;
  private mediaStreamAudioSourceNode: MediaStreamAudioSourceNode | null = null;
  private scriptProcessor: ScriptProcessorNode | null = null;
  private analyserNode: AnalyserNode | null = null;
  private mediaStream: MediaStream | null = null;
  private resampler: StreamResampler | null = null;

  // Playback scheduling
  private nextPlaybackTime: number = 0;
  private activeSources: AudioBufferSourceNode[] = [];
  private playbackQueue: AudioBuffer[] = [];

  // Configuration
  private inputSampleRate: number;
  private outputSampleRate: number;
  private autoGainControl: boolean;
  private echoCancellation: boolean;
  private noiseSuppression: boolean;

  // Callbacks
  private onAudioInput?: (pcm16Data: Uint8Array) => void;
  private onInputError?: (error: Error) => void;

  // Audio processing state
  private isMuted: boolean = false;
  private isListening: boolean = false;

  constructor(config: BrowserAudioConfig = {}) {
    this.inputSampleRate = config.inputSampleRate ?? AUDIO_CONFIG.SAMPLE_RATE;
    this.outputSampleRate = config.outputSampleRate ?? AUDIO_CONFIG.SPEAKER_SAMPLE_RATE;
    this.autoGainControl = config.autoGainControl ?? true;
    this.echoCancellation = config.echoCancellation ?? true;
    this.noiseSuppression = config.noiseSuppression ?? true;
    this.onInputError = config.onInputError;
  }

  /**
   * Initialize the AudioContext and analyser
   */
  async init(analyserConfig?: AnalyserConfig): Promise<void> {
    if (this.audioContext) return; // Already initialized

    const AudioContextClass =
      (window as any).AudioContext || (window as any).webkitAudioContext;
    if (!AudioContextClass) {
      throw new Error('Web Audio API not supported in this browser');
    }

    this.audioContext = new AudioContextClass();

    // Ensure AudioContext is running (not suspended)
    if (!this.audioContext) {
      throw new Error('Failed to initialize AudioContext');
    }

    if (this.audioContext.state === 'suspended') {
      await this.audioContext.resume();
      console.log('👂 AudioContext resumed');
    }

    // Setup analyser for visualization if enabled
    if (analyserConfig?.enabled !== false) {
      this.analyserNode = this.audioContext.createAnalyser();
      this.analyserNode.fftSize = analyserConfig?.fftSize ?? 256;
    }
  }

  /**
   * Start capturing audio from the microphone
   */
  async startMicrophone(
    onAudioInput: (pcm16Data: Uint8Array) => void
  ): Promise<void> {
    if (!this.audioContext) {
      await this.init();
    }

    try {
      this.onAudioInput = onAudioInput;
      this.isListening = true;

      // Request microphone access with constraints
      this.mediaStream = await navigator.mediaDevices.getUserMedia({
        audio: {
          autoGainControl: this.autoGainControl,
          echoCancellation: this.echoCancellation,
          noiseSuppression: this.noiseSuppression,
        },
      });

      // Create source from microphone stream
      this.mediaStreamAudioSourceNode =
        this.audioContext!.createMediaStreamSource(this.mediaStream);

      // Create script processor for PCM extraction
      // Note: ScriptProcessorNode is deprecated but widely supported.
      // AudioWorklet would be better but requires additional setup.
      const bufferSize = 4096;
      this.scriptProcessor = this.audioContext!.createScriptProcessor(
        bufferSize,
        1, // input channels
        1  // output channels
      );

      // Connect the audio graph
      this.mediaStreamAudioSourceNode.connect(this.scriptProcessor);
      this.scriptProcessor.connect(this.audioContext!.destination);

      // Connect to analyser if available
      if (this.analyserNode) {
        this.mediaStreamAudioSourceNode.connect(this.analyserNode);
      }

      // Initialize stateful resampler if sample rates differ
      const hardwareRate = this.audioContext!.sampleRate;
      if (hardwareRate !== this.inputSampleRate) {
        this.resampler = new StreamResampler(hardwareRate, this.inputSampleRate);
      } else {
        this.resampler = null;
      }

      // Handle audio processing
      this.scriptProcessor.onaudioprocess = (event: AudioProcessingEvent) => {
        this._processAudioInput(event);
      };

      console.log('🎤 Microphone started');
    } catch (error) {
      const err = error instanceof Error ? error : new Error(String(error));
      if (this.onInputError) this.onInputError(err);
      throw err;
    }
  }

  /**
   * Internal method to process microphone audio data
   */
  private _processAudioInput(event: AudioProcessingEvent): void {
    if (!this.onAudioInput || !this.audioContext || !this.isListening) return;
    if (this.isMuted) return;

    const inputBuffer = event.inputBuffer;
    const inputData = inputBuffer.getChannelData(0);

    // Silence output to prevent feedback
    const outputBuffer = event.outputBuffer;
    for (let i = 0; i < outputBuffer.getChannelData(0).length; i++) {
      outputBuffer.getChannelData(0)[i] = 0;
    }

    // Resample from hardware rate to target rate if needed
    let processedData: Float32Array = new Float32Array(inputData);

    if (this.resampler) {
      processedData = this.resampler.process(processedData);
    }

    if (processedData.length === 0) return; // Need more data for resampler

    // Convert Float32 to Int16 PCM
    const int16Data = float32ToPcm16(processedData);
    const uint8Data = new Uint8Array(
      int16Data.buffer,
      int16Data.byteOffset,
      int16Data.byteLength
    );

    this.onAudioInput(uint8Data);
  }

  /**
   * Stop capturing microphone input
   */
  stopMicrophone(): void {
    this.isListening = false;

    if (this.mediaStream) {
      this.mediaStream.getTracks().forEach((track) => track.stop());
      this.mediaStream = null;
    }

    if (this.scriptProcessor) {
      this.scriptProcessor.disconnect();
      this.scriptProcessor = null;
    }

    if (this.mediaStreamAudioSourceNode) {
      this.mediaStreamAudioSourceNode.disconnect();
      this.mediaStreamAudioSourceNode = null;
    }

    console.log('🎤 Microphone stopped');
  }

  /**
   * Play back audio received from the server
   * @param pcm16Data Int16 PCM audio data at SPEAKER_SAMPLE_RATE
   */
  playAudio(pcm16Data: Uint8Array): void {
    if (!this.audioContext) {
      console.warn('AudioContext not initialized');
      return;
    }

    if (pcm16Data.length % 2 !== 0) {
      console.warn(`Discarding odd-length PCM buffer (${pcm16Data.length} bytes)`);
      return;
    }

    // Convert Int16 to Float32
    const int16Array = new Int16Array(
      pcm16Data.buffer,
      pcm16Data.byteOffset,
      pcm16Data.length / 2
    );
    const float32Data = pcm16ToFloat32(int16Array);

    // Create audio buffer
    const audioBuffer = this.audioContext.createBuffer(
      1,
      float32Data.length,
      this.outputSampleRate
    );
    audioBuffer.getChannelData(0).set(float32Data);

    // Schedule playback
    this._schedulePlayback(audioBuffer);
  }

  /**
   * Internal method to schedule and play audio with sample-accurate timing
   */
  private _schedulePlayback(audioBuffer: AudioBuffer): void {
    if (!this.audioContext) return;

    const currentTime = this.audioContext.currentTime;
    const duration = audioBuffer.length / this.outputSampleRate;

    // Schedule playback to occur seamlessly after previous audio
    const startTime = Math.max(
      currentTime + 0.01, // Minimum 10ms delay
      this.nextPlaybackTime
    );
    this.nextPlaybackTime = startTime + duration;

    // Create and configure source node
    const source = this.audioContext.createBufferSource();
    source.buffer = audioBuffer;
    source.connect(this.audioContext.destination);

    if (this.analyserNode) {
      source.connect(this.analyserNode);
    }

    source.start(startTime);
    this.activeSources.push(source);

    // Clean up source reference when finished
    source.onended = () => {
      const index = this.activeSources.indexOf(source);
      if (index > -1) {
        this.activeSources.splice(index, 1);
      }
    };
  }

  /**
   * Stop all currently playing audio and clear the queue
   */
  stopPlayback(): void {
    this.activeSources.forEach((source) => {
      try {
        source.stop();
      } catch (e) {
        // Already stopped or other error
      }
    });
    this.activeSources = [];
    this.playbackQueue = [];
    this.nextPlaybackTime = this.audioContext?.currentTime ?? 0;
    console.log('🔇 Playback stopped');
  }

  /**
   * Toggle mute state
   */
  setMuted(muted: boolean): void {
    this.isMuted = muted;
  }

  /**
   * Get current mute state
   */
  isMicMuted(): boolean {
    return this.isMuted;
  }

  /**
   * Get current amplitude from analyser (for visualization)
   * Returns value between 0 and 1
   */
  getAmplitude(): number {
    if (!this.analyserNode) return 0;

    const dataArray = new Uint8Array(this.analyserNode.frequencyBinCount);
    this.analyserNode.getByteTimeDomainData(dataArray);

    const rms = calculateRMS(dataArray);
    return Math.min(rms * 10, 1); // Boost for visualization
  }

  /**
   * Get frequency data from analyser for visualization
   */
  getFrequencyData(): Uint8Array {
    if (!this.analyserNode) {
      return new Uint8Array(0);
    }

    const dataArray = new Uint8Array(this.analyserNode.frequencyBinCount);
    this.analyserNode.getByteFrequencyData(dataArray);
    return dataArray;
  }

  /**
   * Get time-domain data from analyser for waveform visualization
   */
  getWaveformData(): Uint8Array {
    if (!this.analyserNode) {
      return new Uint8Array(0);
    }

    const dataArray = new Uint8Array(this.analyserNode.frequencyBinCount);
    this.analyserNode.getByteTimeDomainData(dataArray);
    return dataArray;
  }

  /**
   * Cleanup and close AudioContext
   */
  cleanup(): void {
    this.stopMicrophone();
    this.stopPlayback();

    if (this.analyserNode) {
      this.analyserNode.disconnect();
      this.analyserNode = null;
    }

    // Don't close AudioContext - it can be expensive to recreate
    // Just leave it suspended if no longer needed
  }

  /**
   * Get current audio context state
   */
  getState(): 'running' | 'suspended' | 'closed' | 'interrupted' | null {
    return this.audioContext?.state ?? null;
  }

  /**
   * Check if microphone is currently listening
   */
  isRecording(): boolean {
    return this.isListening;
  }
}
