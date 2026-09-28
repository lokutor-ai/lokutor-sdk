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
  /**
   * Keep the screen awake while the microphone is live (Screen Wake Lock API), as a video player does.
   * Default true: a phone that dims and locks mid-call suspends the page's microphone and audio, and
   * the call goes silent in both directions. Set false if your app manages this itself.
   */
  keepAwake?: boolean;
  /**
   * A microphone stream the page already asked for, ideally with requestMicrophone() inside the tap that
   * started the call. startMicrophone uses it instead of calling getUserMedia again later, after the
   * connection opens -- which iOS Safari can refuse without showing its prompt.
   */
  microphone?: MediaStream | Promise<MediaStream>;
  onInputError?: (error: Error) => void;
}

/**
 * Ask for the microphone now, from inside the tap or click that starts a call, and return the pending stream.
 *
 * Call it synchronously in the gesture handler, before any await, and pass the result to BrowserAudioManager
 * as `microphone` (ConversationalPanel does this itself). Two iOS Safari behaviours made a later request fail
 * with no prompt at all -- the call connected, the agent spoke, and nothing the caller said was heard:
 *  - a request outside a user gesture may be refused instead of prompting;
 *  - while the page's audio session is in 'playback' mode (set by pages so audio plays through the mute
 *    switch), capture is refused outright. It is put back to 'auto', under which Safari switches to
 *    play-and-record itself when capture starts and keeps the loudspeaker.
 */
export function requestMicrophone(
  constraints: Pick<BrowserAudioConfig, 'autoGainControl' | 'echoCancellation' | 'noiseSuppression'> = {}
): Promise<MediaStream> {
  if (typeof navigator === 'undefined') {
    return Promise.reject(new Error('Microphone capture needs a browser'));
  }
  const session = (navigator as any).audioSession;
  if (session && session.type !== 'auto' && session.type !== 'play-and-record') {
    try { session.type = 'auto'; } catch (_) { /* read-only in this browser */ }
  }
  if (!navigator.mediaDevices?.getUserMedia) {
    return Promise.reject(new Error('Microphone capture is not available here (it needs HTTPS)'));
  }
  return navigator.mediaDevices.getUserMedia({
    audio: {
      autoGainControl: constraints.autoGainControl ?? true,
      echoCancellation: constraints.echoCancellation ?? true,
      noiseSuppression: constraints.noiseSuppression ?? true,
    },
  });
}

/**
 * Resume an AudioContext without waiting on it forever. Outside a user gesture iOS Safari leaves resume()'s
 * promise pending, which stalled the whole call start; the context starts on its own once capture is live,
 * and playback resumes it again anyway.
 */
async function resumeWithin(ctx: AudioContext, ms: number): Promise<void> {
  if (ctx.state === 'running') return;
  await Promise.race([
    ctx.resume().catch(() => {}),
    new Promise<void>((r) => setTimeout(r, ms)),
  ]);
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
  // Reused across getAmplitude() calls instead of allocating a new
  // Uint8Array on every call — this runs once per animation frame (~60/sec)
  // from the visualizer, on the same main thread as audio capture.
  private amplitudeBuffer: Uint8Array<ArrayBuffer> | null = null;
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
  private keepAwake: boolean;
  private providedMicrophone: MediaStream | Promise<MediaStream> | null;

  // Screen wake lock, held while the microphone is live. Browsers release it whenever the page is
  // hidden, so it is taken again when the page becomes visible.
  private wakeLock: { release(): Promise<void> } | null = null;
  private visibilityHandler: (() => void) | null = null;

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
    this.keepAwake = config.keepAwake ?? true;
    this.providedMicrophone = config.microphone ?? null;
    // Never an unhandled rejection: startMicrophone reports it.
    if (this.providedMicrophone instanceof Promise) this.providedMicrophone.catch(() => {});
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

    if (this.audioContext.state !== 'running') {
      await resumeWithin(this.audioContext, 500);
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

    // Already capturing: take the new callback and keep the one pipeline. A second getUserMedia +
    // ScriptProcessor here used to run two captures into the same resampler, doubling and scrambling
    // what the server heard.
    if (this.isListening && this.mediaStream) {
      this.onAudioInput = onAudioInput;
      return;
    }

    try {
      this.onAudioInput = onAudioInput;
      this.isListening = true;

      // The page's own request (made inside the tap), when it has one and its track is still live;
      // otherwise ask now.
      const provided = this.providedMicrophone;
      this.providedMicrophone = null;
      const stream = provided ? await provided : null;
      this.mediaStream = stream && stream.getAudioTracks().some((t) => t.readyState === 'live')
        ? stream
        : await navigator.mediaDevices.getUserMedia({
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
      //
      // 1024 samples is ~21ms at a typical 48kHz hardware rate — matched to
      // AUDIO_CONFIG.CHUNK_DURATION_MS (20ms), the granularity the backend's
      // VAD is tuned around. The previous 4096 was ~85ms of audio buffered
      // before a single byte reached the server: on top of adding raw
      // latency, ScriptProcessorNode callbacks run on the main thread, so
      // that much buffering meant audio could arrive in large, uneven
      // bursts whenever the main thread was briefly busy (a re-render, the
      // visualizer) instead of a steady stream — exactly the kind of
      // mistimed input that can trip server-side VAD into a false barge-in.
      const bufferSize = 1024;
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
      this.holdScreenAwake();
    } catch (error) {
      this.isListening = false;
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

    // While muted, keep sending — silent — chunks instead of sending
    // nothing at all (the old `if (this.isMuted) return` above this).
    // The server's turn-taking/VAD is purely reactive to incoming chunks:
    // if the client stops sending entirely mid-utterance, the server never
    // observes the silence it needs to close out the turn, so muting
    // mid-sentence left the conversation stuck instead of handing off to
    // the agent. Explicitly zeroed here rather than relying solely on the
    // disabled MediaStreamTrack to already read as silence.
    if (this.isMuted) {
      processedData = new Float32Array(processedData.length);
    }

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
    this.releaseScreenAwake();

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
   * Keep the screen on while the microphone is live, and bring audio back when the page returns
   * to the foreground (iOS leaves the context "interrupted" after the screen locks).
   */
  private holdScreenAwake(): void {
    if (typeof document === 'undefined') return;
    if (!this.visibilityHandler) {
      this.visibilityHandler = () => {
        if (document.visibilityState !== 'visible' || !this.isListening) return;
        if (this.audioContext && this.audioContext.state !== 'running') {
          this.audioContext.resume().catch(() => {});
        }
        void this.requestWakeLock();
      };
      document.addEventListener('visibilitychange', this.visibilityHandler);
    }
    void this.requestWakeLock();
  }

  private async requestWakeLock(): Promise<void> {
    if (!this.keepAwake || this.wakeLock || typeof navigator === 'undefined') return;
    const api = (navigator as any).wakeLock;
    if (!api?.request || document.visibilityState !== 'visible') return;
    try {
      const lock = await api.request('screen');
      if (!this.isListening) { await lock.release().catch(() => {}); return; }
      this.wakeLock = lock;
      // The browser drops the lock when the page is hidden; forget it so it is taken again on return.
      lock.addEventListener?.('release', () => { if (this.wakeLock === lock) this.wakeLock = null; });
    } catch (_) {
      // Not allowed right now (battery saver, permissions policy): the call works, the screen may dim.
    }
  }

  private releaseScreenAwake(): void {
    if (this.visibilityHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
    const lock = this.wakeLock;
    this.wakeLock = null;
    lock?.release().catch(() => {});
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

    if (this.audioContext.state === 'suspended') {
      this.audioContext.resume();
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
    if (this.mediaStream) {
      this.mediaStream.getAudioTracks().forEach(track => {
        track.enabled = !muted;
      });
    }
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

    if (!this.amplitudeBuffer || this.amplitudeBuffer.length !== this.analyserNode.frequencyBinCount) {
      this.amplitudeBuffer = new Uint8Array(this.analyserNode.frequencyBinCount);
    }
    this.analyserNode.getByteTimeDomainData(this.amplitudeBuffer);

    const rms = calculateRMS(this.amplitudeBuffer);
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

    // Closed, not left open: a page makes a new manager per call, so every open context was one more
    // left running for the life of the page (iOS Safari showed five at the start of a second call, which
    // then hung). init() makes a fresh one if this manager is used again.
    if (this.audioContext) {
      const ctx = this.audioContext;
      this.audioContext = null;
      this.nextPlaybackTime = 0;
      ctx.close().catch(() => {});
    }
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
