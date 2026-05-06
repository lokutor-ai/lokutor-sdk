const PANEL_CSS = /*css*/ `
  .cv-panel {
    position: relative;
    width: 100%;
    aspect-ratio: 16 / 9;
    max-height: 800px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 0;
    margin: 0;
    background: var(--cv-bg, #0a0a0a);
    box-shadow: inset 0 10px 40px rgba(0, 0, 0, 0.1), inset 0 0 100px rgba(0, 0, 0, 0.05);
    border-radius: 40px;
    overflow: hidden;
  }
  .cv-curtain {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background: var(--cv-bg, #0a0a0a);
    z-index: 100;
    transition: transform 1s cubic-bezier(0.16, 1, 0.3, 1);
  }
  .cv-curtain .cv-curtain-bg {
    position: absolute;
    inset: 0;
    background: url('/background_gradient.jpeg') center / cover no-repeat;
    z-index: -1;
  }
  .cv-curtain .cv-curtain-overlay {
    position: absolute;
    inset: 0;
    background: var(--cv-accent);
    opacity: 0.35;
    z-index: -1;
  }
  .cv-curtain.is-up { transform: translateY(-100%); }
  .cv-curtain-content {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: clamp(0.75rem, 1.5vw, 1.5rem);
    color: #fff;
    text-align: center;
    z-index: 2;
    padding: clamp(1rem, 2vw, 2rem);
  }
  .cv-curtain-title {
    font-size: clamp(1.2rem, 4vw, 2.5rem);
    font-weight: 800;
    letter-spacing: -0.03em;
    margin: 0;
    text-shadow: 0 4px 20px rgba(0,0,0,0.3);
  }
  .cv-curtain-desc {
    font-size: clamp(0.75rem, 2vw, 1.1rem);
    opacity: 0.8;
    color: rgba(255,255,255,0.8);
    max-width: 400px;
    margin: 0;
    line-height: 1.5;
  }
  .cv-curtain-btn {
    margin-top: 1rem;
    padding: clamp(0.6rem, 1.5vw, 1rem) clamp(1.5rem, 3vw, 2.5rem);
    border-radius: 100px;
    background: #fff;
    color: #000;
    border: none;
    font-weight: 700;
    font-size: clamp(0.8rem, 1.5vw, 1rem);
    display: flex;
    align-items: center;
    gap: 0.75rem;
    cursor: pointer;
    transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1);
    box-shadow: 0 10px 30px rgba(0,0,0,0.2);
  }
  .cv-curtain-btn:hover {
    transform: scale(1.05);
    background: #f0f0f0;
    box-shadow: 0 15px 40px rgba(0,0,0,0.3);
  }
  .cv-curtain-btn svg { transition: transform 0.3s ease; }
  .cv-curtain-btn:hover svg { transform: translateX(4px); }
  .cv-header {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 0.25rem;
    z-index: 20;
    margin-bottom: auto;
    padding-top: 1.5rem;
  }
  .cv-title {
    font-size: clamp(1rem, 2.5vw, 1.75rem);
    font-weight: 700;
    color: var(--cv-text, #e0e0e0);
    display: flex;
    align-items: center;
    gap: 1rem;
    letter-spacing: -0.02em;
  }
  .cv-title .cv-timer {
    font-variant-numeric: tabular-nums;
    color: var(--cv-accent);
    font-weight: 400;
    opacity: 0.8;
  }
  .cv-visualizer-wrap {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    width: clamp(140px, 40vw, 280px);
    height: clamp(140px, 40vw, 280px);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 10;
    transition: all 0.5s cubic-bezier(0.16, 1, 0.3, 1);
  }
  .cv-is-speaking .cv-visualizer-wrap {
    animation: cv-pulse 2.5s infinite ease-in-out;
  }
  .cv-is-thinking .cv-visualizer-wrap {
    opacity: 0.5;
    transform: translate(-50%, -50%) scale(0.9);
  }
  @keyframes cv-pulse {
    0%, 100% { transform: translate(-50%, -50%) scale(1); }
    50% { transform: translate(-50%, -50%) scale(1.05); }
  }
  .cv-canvas {
    width: 100% !important;
    height: 100% !important;
    position: relative;
    z-index: 0;
  }
  .cv-canvas {
    width: 100% !important;
    height: 100% !important;
    position: relative;
    z-index: 0;
  }
  .cv-controls {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 1.5rem;
    margin-top: auto;
    padding-bottom: 1.5rem;
    z-index: 20;
  }
  .cv-pill {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    background: var(--cv-ui-bg, rgba(255, 255, 255, 0.03));
    backdrop-filter: blur(30px);
    -webkit-backdrop-filter: blur(30px);
    border: 1px solid var(--cv-ui-border, rgba(255, 255, 255, 0.08));
    padding: 0.3rem 0.75rem;
    border-radius: 100px;
    box-shadow: 0 20px 50px rgba(0, 0, 0, 0.15);
  }
  .cv-btn {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    background: transparent;
    border: none;
    color: var(--cv-text-dim, rgba(255,255,255,0.55));
    font-weight: 600;
    font-size: 0.75rem;
    cursor: pointer;
    transition: all 0.2s ease;
    padding: 0.35rem 0.75rem;
    border-radius: 50px;
  }
  .cv-btn:hover { color: var(--cv-text, #fff); background: var(--cv-ui-bg, rgba(255,255,255,0.05)); }
  .cv-btn--end { color: #ff4444; }
  .cv-btn--end .cv-btn-box {
    background: #ff4444;
    width: 8px;
    height: 8px;
    border-radius: 2px;
  }
  .cv-btn.is-muted { color: var(--cv-accent); }
  .cv-error {
    position: absolute;
    bottom: 1.25rem;
    left: 50%;
    transform: translateX(-50%);
    background: #0a0a0a;
    color: #e0e0e0;
    padding: 0.75rem 1.5rem;
    border-radius: 12px;
    font-size: 0.875rem;
    font-weight: 500;
    display: none;
    align-items: center;
    gap: 0.75rem;
    box-shadow: 0 10px 40px rgba(0, 0, 0, 0.2);
    z-index: 1000;
    border: 1px solid rgba(255, 255, 255, 0.06);
    backdrop-filter: blur(20px);
  }
  .cv-error.is-visible { display: flex; }
  .cv-error-icon { color: var(--cv-accent); flex-shrink: 0; }
  @media (max-width: 640px) {
    .cv-panel { aspect-ratio: 3 / 4; max-height: 600px; border-radius: 20px; }
    .cv-curtain-content { padding: 0.75rem; gap: 0.5rem; }
    .cv-curtain-title { font-size: clamp(1rem, 5vw, 1.6rem); }
    .cv-curtain-btn { padding: 0.5rem 1.25rem; font-size: clamp(0.7rem, 2vw, 0.85rem); }
    .cv-visualizer-wrap { width: clamp(100px, 50vw, 200px); height: clamp(100px, 50vw, 200px); }
  }
`;

let styleInjected = false;

function injectStyles() {
  if (styleInjected) return;
  const style = document.createElement('style');
  style.textContent = PANEL_CSS;
  document.head.appendChild(style);
  styleInjected = true;
}

export interface ConversationalPanelConfig {
  container: HTMLElement;
  title: string;
  description: string;
  prompt: string;
  voice?: string;
  language?: string;
  /** Accent color for the sphere glow and highlights, e.g. '#e74c3c' */
  accentColor?: string;
  /** Background color, e.g. '#0a0a0a' */
  backgroundColor?: string;
  /** API key for connecting to the voice agent */
  apiKey: string;
  /** Reference to the ConvoAgent class */
  ConvoAgent: any;
  /** Reference to the SphereVisualizer class */
  SphereVisualizer: any;
  /** URL for the connecting sound asset */
  connectingSoundSrc?: string;
  /** Background image for the curtain, e.g. '/background_gradient.jpeg' */
  curtainBgSrc?: string;
  /** Tool definitions for LLM function calling */
  tools?: any[];
  /** Maximum conversation duration in seconds (default 300) */
  maxDuration?: number;
  /** Seconds of silence before auto-close (default 30) */
  silenceTimeout?: number;
}

export class ConversationalPanel {
  private cfg: ConversationalPanelConfig;
  private container: HTMLElement;
  private agent: any = null;
  private visualizer: any = null;
  private timerTicker: number | null = null;
  private connectingSound: HTMLAudioElement | null = null;
  private connectingFadeTimer: number | null = null;
  private isRunning = false;
  private _locked = false;
  private _lastSpeechTime = 0;

  // Cached DOM refs
  private el!: HTMLElement;
  private curtain!: HTMLElement;
  private curtainTitle!: HTMLElement;
  private curtainDesc!: HTMLElement;
  private startBtn!: HTMLButtonElement;
  private errorEl!: HTMLElement;
  private errorText!: HTMLElement;
  private timerEl!: HTMLElement;
  private canvas!: HTMLCanvasElement;
  private muteBtn!: HTMLElement;
  private muteSvg!: SVGElement;
  private stopBtn!: HTMLElement;
  private visualizerWrap!: HTMLElement;

  // Callbacks
  onTranscription?: (text: string) => void;
  onResponse?: (text: string) => void;
  onStart?: () => void;
  onStop?: () => void;
  onError?: (err: any) => void;
  onToolCall?: (tool: any) => void;

  constructor(cfg: ConversationalPanelConfig) {
    this.cfg = cfg;
    this.container = cfg.container;
    injectStyles();
    this.buildDOM();
  }

  private buildDOM() {
    const accent = this.cfg.accentColor || '#a25a6b';
    const bg = this.cfg.backgroundColor || '#0a0a0a';

    this.container.style.setProperty('--cv-accent', accent);
    this.setBackgroundColor(bg);

    this.el = document.createElement('div');
    this.el.className = 'cv-panel';
    this.el.innerHTML = `
      <div class="cv-curtain">
        <div class="cv-curtain-bg"></div>
        <div class="cv-curtain-overlay"></div>
        <div class="cv-curtain-content">
          <h3 class="cv-curtain-title">${this.esc(this.cfg.title)}</h3>
          <p class="cv-curtain-desc">${this.esc(this.cfg.description)}</p>
          <button class="cv-curtain-btn">
            <span>Start Conversation</span>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" width="20" height="20">
              <path d="M5 12h14M12 5l7 7-7 7"/>
            </svg>
          </button>
        </div>
        <div class="cv-error">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
            width="20" height="20" class="cv-error-icon">
            <circle cx="12" cy="12" r="10"></circle>
            <path d="M12 8v4"></path>
            <path d="M12 16h.01"></path>
          </svg>
          <span class="cv-error-text"></span>
        </div>
      </div>
      <div class="cv-header">
        <h2 class="cv-title">${this.esc(this.cfg.title)} <span class="cv-timer">00:00</span></h2>
      </div>
      <div class="cv-visualizer-wrap">
        <canvas class="cv-canvas"></canvas>
      </div>
      <div class="cv-controls">
        <div class="cv-pill">
          <button class="cv-btn cv-btn--mute">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" width="18" height="18">
              <path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/>
              <path d="M19 10v2a7 7 0 0 1-14 0v-2"/>
            </svg>
            <span>Mute</span>
          </button>
          <button class="cv-btn cv-btn--end">
            <div class="cv-btn-box"></div>
            <span>End call</span>
          </button>
        </div>
      </div>
    `;

    this.container.appendChild(this.el);

    // Cache refs
    this.curtain = this.el.querySelector('.cv-curtain')!;
    if (this.cfg.curtainBgSrc) {
      this.curtain.style.backgroundImage = `url('${this.cfg.curtainBgSrc}')`;
    }
    this.curtainTitle = this.el.querySelector('.cv-curtain-title')!;
    this.curtainDesc = this.el.querySelector('.cv-curtain-desc')!;
    this.startBtn = this.el.querySelector('.cv-curtain-btn')!;
    this.errorEl = this.el.querySelector('.cv-error')!;
    this.errorText = this.el.querySelector('.cv-error-text')!;
    this.timerEl = this.el.querySelector('.cv-timer')!;
    this.canvas = this.el.querySelector('.cv-canvas')!;
    this.muteBtn = this.el.querySelector('.cv-btn--mute')!;
    this.muteSvg = this.muteBtn.querySelector('svg')!;
    this.stopBtn = this.el.querySelector('.cv-btn--end')!;
    this.visualizerWrap = this.el.querySelector('.cv-visualizer-wrap')!;

    // Events
    this.startBtn.addEventListener('click', () => this.start());
    this.stopBtn.addEventListener('click', () => this.stop());
    this.muteBtn.addEventListener('click', () => this.toggleMute());
  }

  private esc(s: string) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  /** Start the conversation (called when user clicks "Start Conversation" or externally) */
  async start() {
    if (this.isRunning) return;
    if (this._locked) {
      this.showError('This session has ended. Refresh the page to start a new one.');
      return;
    }
    this.isRunning = true;
    this.startBtn.disabled = true;
    this.errorEl.classList.remove('is-visible');
    this.playConnectingSound();

    // Freeze panel dimensions so content changes can't shift layout
    const rect = this.container.getBoundingClientRect();
    this.container.style.width = rect.width + 'px';
    this.container.style.height = rect.height + 'px';

    try {
      const ConvoAgent = this.cfg.ConvoAgent;
      const SphereVisualizer = this.cfg.SphereVisualizer;

      const visualizer = new SphereVisualizer(this.canvas);
      visualizer.resize(280, 280);
      this.visualizer = visualizer;

      // Pass accent color to the visualizer shader
      if (this.cfg.accentColor && typeof visualizer.setAccentColor === 'function') {
        const hex = this.cfg.accentColor;
        visualizer.setAccentColor(
          parseInt(hex.slice(1, 3), 16) / 255,
          parseInt(hex.slice(3, 5), 16) / 255,
          parseInt(hex.slice(5, 7), 16) / 255
        );
      }

      const agentHandle = new ConvoAgent({
        apiKey: this.cfg.apiKey,
        tools: this.cfg.tools,
        prompt: this.cfg.prompt,
        voice: this.cfg.voice || 'M1',
        language: this.cfg.language || 'en',
        onStatusChange: (status: string) => {
          this.el.classList.remove('cv-is-speaking', 'cv-is-thinking');
          if (status === 'speaking') this.el.classList.add('cv-is-speaking');
          else if (status === 'thinking') this.el.classList.add('cv-is-thinking');
        },
        onTranscription: (text: string) => {
          this.onTranscription?.(text);
        },
        onResponse: (text: string) => {
          this._lastSpeechTime = Date.now();
          this.onResponse?.(text);
        },
        onToolCall: (tool: any) => {
          this.onToolCall?.(tool);
        },
        onError: (err: any) => {
          if (!this.isRunning) return;
          this.onError?.(err);
          this.stop();
          this.showError('Connection issue. Try again in a moment.');
        }
      });

      this.agent = agentHandle;
      visualizer.setAudioClient(agentHandle);

      // Lift curtain immediately for responsive UI
      this.curtain.classList.add('is-up');
      try { agentHandle.unlockAudioForMobile?.(); } catch (_) { /* ignore */ }

      const ok = await agentHandle.connect();
      if (ok) {
        this.fadeOutConnectingSound();
        // Resize canvas to container
        const wrap = this.visualizerWrap;
        if (wrap) {
          const dpr = Math.min(window.devicePixelRatio || 1, 2);
          const w = Math.floor(wrap.clientWidth * dpr);
          const h = Math.floor(wrap.clientHeight * dpr);
          visualizer.resize(w, h);
        }
        visualizer.start();
        this.startTimer();
        this.onStart?.();
        if ((window as any).lucide) (window as any).lucide.createIcons();
      } else {
        this.fadeOutConnectingSound();
        this.playErrorTone();
        this.curtain.classList.remove('is-up');
        this.showError('Could not connect. Please try again.');
        this.isRunning = false;
      }
    } catch (err: any) {
      this.fadeOutConnectingSound();
      this.playErrorTone();
      console.error('ConversationalPanel start error:', err);
      this.stop();
      this.showError('Something went wrong. Please try again.');
    } finally {
      this.startBtn.disabled = false;
    }
  }

  /** Stop / disconnect the conversation */
  stop() {
    if (!this.isRunning && !this.agent && !this.visualizer) return;
    this.isRunning = false;
    this.fadeOutConnectingSound();
    this.errorEl.classList.remove('is-visible');
    if (this.agent) {
      try { this.agent.disconnect(); } catch (_) {}
      this.agent = null;
    }
    if (this.timerTicker) { clearInterval(this.timerTicker); this.timerTicker = null; }
    if (this.visualizer) { this.visualizer.stop(); this.visualizer = null; }
    this.curtain.classList.remove('is-up');
    this.el.classList.remove('cv-is-speaking', 'cv-is-thinking');
    this.muteBtn.classList.remove('is-muted');
    const span = this.muteBtn.querySelector('span');
    if (span) span.textContent = 'Mute';
    this.onStop?.();

    // Ensure start button is enabled
    this.startBtn.disabled = false;
  }

  /** Update accent color dynamically */
  setColor(color: string) {
    this.cfg.accentColor = color;
    this.container.style.setProperty('--cv-accent', color);
    if (this.visualizer && typeof this.visualizer.setAccentColor === 'function') {
      const r = parseInt(color.slice(1, 3), 16) / 255;
      const g = parseInt(color.slice(3, 5), 16) / 255;
      const b = parseInt(color.slice(5, 7), 16) / 255;
      this.visualizer.setAccentColor(r, g, b);
    }
  }

  /** Update background color dynamically and ensure text/UI contrast */
  setBackgroundColor(color: string) {
    this.cfg.backgroundColor = color;
    this.container.style.setProperty('--cv-bg', color);

    // Calculate luminance to decide on light/dark theme contrast
    // Simple hex to RGB conversion
    let r = 0, g = 0, b = 0;
    if (color.startsWith('#')) {
      if (color.length === 4) {
        r = parseInt(color[1] + color[1], 16);
        g = parseInt(color[2] + color[2], 16);
        b = parseInt(color[3] + color[3], 16);
      } else {
        r = parseInt(color.slice(1, 3), 16);
        g = parseInt(color.slice(3, 5), 16);
        b = parseInt(color.slice(5, 7), 16);
      }
    }

    // Relative luminance formula
    const luminance = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
    const isLight = luminance > 0.5;

    // Set contrast variables
    this.container.style.setProperty('--cv-text', isLight ? '#000000' : '#ffffff');
    this.container.style.setProperty('--cv-text-dim', isLight ? 'rgba(0,0,0,0.6)' : 'rgba(255,255,255,0.6)');
    this.container.style.setProperty('--cv-ui-bg', isLight ? 'rgba(0,0,0,0.05)' : 'rgba(255,255,255,0.05)');
    this.container.style.setProperty('--cv-ui-border', isLight ? 'rgba(0,0,0,0.1)' : 'rgba(255,255,255,0.1)');
  }

  /** Update title text */
  setTitle(title: string) {
    this.cfg.title = title;
    this.curtainTitle.textContent = title;
    const h2 = this.el.querySelector('.cv-title');
    if (h2) h2.innerHTML = `${this.esc(title)} <span class="cv-timer">${this.timerEl?.textContent || '00:00'}</span>`;
  }

  /** Update description text */
  setDescription(desc: string) {
    this.cfg.description = desc;
    this.curtainDesc.textContent = desc;
  }

  /** Update prompt (only takes effect on next start()) */
  setPrompt(prompt: string) {
    this.cfg.prompt = prompt;
  }

  /** Update tool definitions (only takes effect on next start()) */
  setTools(tools: any[]) {
    this.cfg.tools = tools;
  }

  /** Show an error message */
  showError(text: string) {
    this.errorText.textContent = text;
    this.errorEl.classList.add('is-visible');
    setTimeout(() => {
      this.errorEl.classList.remove('is-visible');
    }, 5000);
  }

  /** Destroy the component, removing all DOM and stopping any active session */
  destroy() {
    this.stop();
    this.el.remove();
  }

  // ─── internal helpers ────────────────────────────────────

  private startTimer() {
    let elapsed = 0;
    const maxDur = this.cfg.maxDuration || 300;
    const silentMax = this.cfg.silenceTimeout || 30;
    this._lastSpeechTime = Date.now();
    this.timerEl.textContent = '00:00';
    this.timerTicker = window.setInterval(() => {
      elapsed++;
      const m = Math.floor(elapsed / 60).toString().padStart(2, '0');
      const s = (elapsed % 60).toString().padStart(2, '0');
      this.timerEl.textContent = `${m}:${s}`;

      // Check max duration
      if (elapsed >= maxDur) {
        this._locked = true;
        this.stop();
        this.showError('Conversation time limit reached. Refresh to start a new one.');
        return;
      }

      // Check silence timeout
      if (Date.now() - this._lastSpeechTime > silentMax * 1000) {
        this._locked = true;
        this.stop();
        this.showError('Session ended due to inactivity. Refresh to start a new one.');
      }
    }, 1000);
  }

  private toggleMute() {
    if (!this.agent) return;
    try {
      const muted = this.agent.toggleMute();
      this.muteBtn.classList.toggle('is-muted', muted);
      const span = this.muteBtn.querySelector('span');
      if (span) span.textContent = muted ? 'Unmute' : 'Mute';
      if (this.muteSvg) {
        this.muteSvg.innerHTML = muted
          ? '<path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="23" y1="1" x2="1" y2="23"/>'
          : '<path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/>';
      }
    } catch (_) {}
  }

  private playConnectingSound() {
    this.fadeOutConnectingSound();
    const src = this.cfg.connectingSoundSrc || '/connecting.mp3';
    this.connectingSound = new Audio(src);
    this.connectingSound.volume = 0.6;
    this.connectingSound.play().catch(() => {});
  }

  private fadeOutConnectingSound() {
    if (this.connectingFadeTimer) { clearInterval(this.connectingFadeTimer); this.connectingFadeTimer = null; }
    if (!this.connectingSound) return;
    const startVol = this.connectingSound.volume;
    const steps = 15;
    let step = 0;
    this.connectingFadeTimer = window.setInterval(() => {
      step++;
      const progress = step / steps;
      if (this.connectingSound) this.connectingSound.volume = startVol * Math.max(0, 1 - progress);
      if (step >= steps) {
        if (this.connectingFadeTimer) { clearInterval(this.connectingFadeTimer); this.connectingFadeTimer = null; }
        if (this.connectingSound) { this.connectingSound.pause(); this.connectingSound.currentTime = 0; this.connectingSound = null; }
      }
    }, 100);
  }

  private playErrorTone() {
    try {
      const ctx = new ((window as any).AudioContext || (window as any).webkitAudioContext)();
      const now = ctx.currentTime;
      [0, 0.3].forEach((offset, i) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain); gain.connect(ctx.destination);
        osc.type = 'sine';
        osc.frequency.setValueAtTime(600 - i * 200, now + offset);
        gain.gain.setValueAtTime(0.25, now + offset);
        gain.gain.exponentialRampToValueAtTime(0.001, now + offset + 0.35);
        osc.start(now + offset); osc.stop(now + offset + 0.4);
      });
    } catch (_) {}
  }
}
