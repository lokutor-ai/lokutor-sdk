type RGB = [number, number, number];

function rgbToHsl(r: number, g: number, b: number): [number, number, number] {
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    let h = 0, s = 0;
    const l = (max + min) / 2;
    const d = max - min;
    if (d !== 0) {
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        switch (max) {
            case r: h = (g - b) / d + (g < b ? 6 : 0); break;
            case g: h = (b - r) / d + 2; break;
            default: h = (r - g) / d + 4; break;
        }
        h /= 6;
    }
    return [h, s, l];
}

function hslToRgb(h: number, s: number, l: number): RGB {
    h = ((h % 1) + 1) % 1;
    if (s === 0) return [l, l, l];
    const hue2rgb = (p: number, q: number, t: number) => {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1 / 6) return p + (q - p) * 6 * t;
        if (t < 1 / 2) return q;
        if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6;
        return p;
    };
    const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
    const p = 2 * l - q;
    return [hue2rgb(p, q, h + 1 / 3), hue2rgb(p, q, h), hue2rgb(p, q, h - 1 / 3)];
}

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

export type OrbState = 'idle' | 'listening' | 'user' | 'thinking' | 'agent';
type State = OrbState;

// How the orb carries itself in each state; it eases from one to the next, never jumps. The ball
// itself never turns: only the stains on it move.
//   flow: how fast the stains drift with nobody speaking   voice: how much a voice speeds them up
//   depth: how strongly the stains show                     gain: brightness of the lit side
interface Pose { flow: number; voice: number; depth: number; gain: number; contrast: number }
const POSES: Record<State, Pose> = {
    idle: { flow: 0.05, voice: 0, depth: 0.85, gain: 1.0, contrast: 1.1 },
    listening: { flow: 0.05, voice: 0, depth: 0.85, gain: 1.0, contrast: 1.1 },
    user: { flow: 0.05, voice: 2.2, depth: 0.95, gain: 1.05, contrast: 1.1 },
    // Thinking: nobody is speaking, so the stains churn on their own, slowly.
    thinking: { flow: 0.45, voice: 0, depth: 1.1, gain: 0.95, contrast: 1.15 },
    agent: { flow: 0.05, voice: 2.6, depth: 1.0, gain: 1.2, contrast: 1.05 },
};

// Two inks per state. The agent speaks in its accent; whoever is on the other end has the opposite
// colour on the wheel (as the platform's visualizer always did), so it is plain at a glance whose turn
// it is. While it is your turn and you are quiet, your colour rests; while the agent thinks, its own
// colour is quiet. With no call the orb is the agent, waiting: its accent, at rest. On a light stage
// the dark ink is lifted, so the ball reads as light on light.
export function orbInks([r, g, b]: RGB, lightStage = false): Record<State, { ink: RGB; paper: RGB }> {
    const [h] = rgbToHsl(clamp01(r), clamp01(g), clamp01(b));
    const you = (h + 0.5) % 1;
    if (lightStage) {
        return {
            idle: { ink: hslToRgb(h, 0.3, 0.5), paper: hslToRgb(h, 0.42, 0.88) },
            listening: { ink: hslToRgb(you, 0.26, 0.52), paper: hslToRgb(you, 0.34, 0.9) },
            user: { ink: hslToRgb(you, 0.38, 0.42), paper: hslToRgb(you, 0.5, 0.84) },
            thinking: { ink: hslToRgb(h, 0.22, 0.55), paper: hslToRgb(h, 0.3, 0.9) },
            agent: { ink: hslToRgb(h, 0.45, 0.36), paper: hslToRgb(h + 0.03, 0.7, 0.84) },
        };
    }
    return {
        idle: { ink: hslToRgb(h, 0.38, 0.08), paper: hslToRgb(h, 0.48, 0.79) },
        listening: { ink: hslToRgb(you, 0.3, 0.08), paper: hslToRgb(you, 0.35, 0.8) },
        user: { ink: hslToRgb(you, 0.4, 0.09), paper: hslToRgb(you, 0.55, 0.76) },
        thinking: { ink: hslToRgb(h, 0.3, 0.08), paper: hslToRgb(h, 0.35, 0.78) },
        agent: { ink: hslToRgb(h, 0.55, 0.09), paper: hslToRgb(h + 0.03, 0.78, 0.79) },
    };
}

// A person at rest breathes 12-15 times a minute; the orb's idle swell follows that.
const BREATH_SECONDS = 4.6;
// Pixels across the canvas. Fixed: a grid that changes size pops instead of easing. A small orb
// passes fewer with data-cells on its canvas, or its pixels get too fine to read as pixels.
const CELLS = 116;
const LEVELS = 3;

const FRAGMENT = `
    precision highp float;
    uniform vec2 uRes;
    uniform float uFlow, uDepth, uGain, uContrast, uIn, uOut, uBreath, uOff;
    uniform float uCells, uLevels;
    uniform vec3 uInk, uPaper;

    float hash13(vec3 p) { p = fract(p * 0.1031); p += dot(p, p.zyx + 31.32); return fract((p.x + p.y) * p.z); }
    float vnoise(vec3 p) {
        vec3 i = floor(p), f = fract(p);
        f = f * f * (3.0 - 2.0 * f);
        return mix(mix(mix(hash13(i), hash13(i + vec3(1,0,0)), f.x),
                       mix(hash13(i + vec3(0,1,0)), hash13(i + vec3(1,1,0)), f.x), f.y),
                   mix(mix(hash13(i + vec3(0,0,1)), hash13(i + vec3(1,0,1)), f.x),
                       mix(hash13(i + vec3(0,1,1)), hash13(i + vec3(1,1,1)), f.x), f.y), f.z);
    }
    float fbm(vec3 p) { float s = 0.0, a = 0.5; for (int k = 0; k < 4; k++) { s += a * vnoise(p); p = p * 2.02 + 7.3; a *= 0.5; } return s / 0.9375; }

    // Ordered dithering threshold, 8x8 Bayer built from the 2x2 one.
    float bayer2(vec2 a) { a = floor(a); return fract(a.x * 0.5 + a.y * a.y * 0.75); }
    float bayer8(vec2 a) { return bayer2(a * 0.25) * 0.0625 + bayer2(a * 0.5) * 0.25 + bayer2(a); }

    void main() {
        // Every pixel of the grid is one flat square: shade it once, at its centre.
        float cellPx = max(min(uRes.x, uRes.y) / uCells, 1.0);
        vec2 pix = floor(gl_FragCoord.xy / cellPx);
        vec2 uv = ((pix + 0.5) * cellPx * 2.0 - uRes) / min(uRes.x, uRes.y);
        float R = 0.86 + 0.015 * uBreath + 0.025 * uOut;
        vec2 p = uv / R;
        float r2 = dot(p, p);
        if (r2 >= 1.0) { gl_FragColor = vec4(0.0); return; }

        vec3 n = vec3(p, sqrt(1.0 - r2));
        vec3 s = n;
        s.yz = mat2(0.94, -0.34, 0.34, 0.94) * s.yz;

        // Stains on the ball: noise warped by noise. uFlow advances with the voice, so they flow
        // on every syllable and settle in the pauses.
        vec3 q = s * 1.7;
        float w = fbm(q + vec3(0.0, uFlow * 0.35, uFlow * 0.12));
        float clouds = fbm(q * 1.6 + vec3(w * 2.2, -uFlow * 0.25, w * 1.1));
        // The caller's voice stirs the surface; the agent's lights it.
        float depth = uDepth * (1.0 + 0.6 * uIn);
        float surface = clamp(0.5 + (clouds - 0.5) * 2.2 * depth, 0.0, 1.0);

        float key = clamp(dot(n, normalize(vec3(-0.5, 0.55, 0.67))), 0.0, 1.0);
        float rim = pow(1.0 - n.z, 2.4);
        float lum = surface * (0.22 + 0.95 * key) + 0.32 * rim;
        lum = pow(clamp(lum * uGain * (0.88 + 0.45 * uOut), 0.0, 1.0), uContrast);

        float steps = max(uLevels - 1.0, 1.0);
        float tone = clamp(floor(lum * steps + bayer8(pix)) / steps, 0.0, 1.0);
        vec3 col = mix(uInk, uPaper, tone);
        if (uOff > 0.5) col = vec3(dot(col, vec3(0.299, 0.587, 0.114)) * 0.55);
        gl_FragColor = vec4(col, 1.0);
    }
`;

/**
 * The voice of a live conversation, and ConversationalPanel's own visualizer: a lit ball drawn in
 * chunky, dithered pixels, two inks per state. It never turns. The stains on it flow at the pace of
 * whoever is speaking and settle when they pause: the caller's colour (the accent's opposite on the
 * colour wheel) while it is their turn, the accent while the agent speaks, the accent quietly while it
 * thinks. With no call it idles, breathing, so the panel shows the same orb before, during and after a
 * call. Light or dark inks follow the background it is drawn on.
 *
 * Usable on its own: `new PixelOrb(canvas).start()` draws the waiting orb; `setAudioClient(agent)`
 * makes it follow a call (anything with `getAmplitude()` and `isAgentSpeakingNow()`).
 */
export class PixelOrb {
    /** Draws with no call (idling), so ConversationalPanel shows it while waiting. */
    static readonly idles = true;

    private canvas: HTMLCanvasElement;
    private gl: WebGLRenderingContext | null = null;
    private program: WebGLProgram | null = null;
    private uniforms: Record<string, WebGLUniformLocation | null> = {};
    private animationId: number | null = null;
    private lastFrame = 0;
    private isActive = false;
    private currentAmplitude = 0;
    private audioClient: any = null;
    private smoothedAmplitude = 0;
    private isDeactivated = false;
    // 1 = agent speaking, 2 = the caller's turn (speaking or not).
    private speakingMode = 2;
    private thinkingFlag = false;
    private accent: RGB = [0xa2 / 255, 0x52 / 255, 0x5e / 255];
    private lightStage = false;
    private palette = orbInks(this.accent);
    // Eased state: the pose, the inks, the two voice levels, and the integrated flow phase
    // (integrated so a change of pace never makes the stains jump).
    private pose: Pose = { ...POSES.idle };
    private ink: RGB = this.palette.idle.ink;
    private paper: RGB = this.palette.idle.paper;
    private inVol = 0;
    private outVol = 0;
    private flow = 0;
    private timeScale = 1;
    private drawn = false;
    private cells = CELLS;

    constructor(canvas: HTMLCanvasElement) {
        this.canvas = canvas;
        const cells = Number(canvas.dataset.cells);
        if (cells >= 16) this.cells = cells;
        try {
            if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) this.timeScale = 0.35;
        } catch { /* older browsers */ }
        this.initWebGL();
    }

    private initWebGL() {
        const gl = this.canvas.getContext('webgl', { premultipliedAlpha: true, antialias: false });
        if (!gl) {
            console.error('WebGL not supported');
            return;
        }
        this.gl = gl;
        const vs = this.createShader(gl.VERTEX_SHADER, 'attribute vec2 position; void main() { gl_Position = vec4(position, 0.0, 1.0); }');
        const fs = this.createShader(gl.FRAGMENT_SHADER, FRAGMENT);
        if (!vs || !fs) return;
        const program = gl.createProgram()!;
        gl.attachShader(program, vs);
        gl.attachShader(program, fs);
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
            console.error('Program link error:', gl.getProgramInfoLog(program));
            return;
        }
        this.program = program;
        for (const name of ['uRes', 'uFlow', 'uDepth', 'uGain', 'uContrast', 'uIn', 'uOut', 'uBreath', 'uOff', 'uCells', 'uLevels', 'uInk', 'uPaper']) {
            this.uniforms[name] = gl.getUniformLocation(program, name);
        }
        const buffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
        const loc = gl.getAttribLocation(program, 'position');
        gl.enableVertexAttribArray(loc);
        gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
    }

    private createShader(type: number, source: string): WebGLShader | null {
        const gl = this.gl!;
        const shader = gl.createShader(type)!;
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
            console.error('Shader compile error:', gl.getShaderInfoLog(shader));
            gl.deleteShader(shader);
            return null;
        }
        return shader;
    }

    private render = () => {
        const gl = this.gl;
        if (!gl || !this.program || !this.isActive) return;
        const wall = Date.now();
        // Easing in seconds, not frames, so a throttled tab moves the same way.
        const dt = Math.min(0.25, (wall - (this.lastFrame || wall)) / 1000) * this.timeScale;
        this.lastFrame = wall;
        const ease = (tau: number) => 1 - Math.exp(-dt / tau);

        if (this.audioClient && typeof this.audioClient.getAmplitude === 'function') {
            const raw = this.audioClient.getAmplitude();
            this.smoothedAmplitude += (raw - this.smoothedAmplitude) * ease(raw > this.smoothedAmplitude ? 0.08 : 0.3);
            this.currentAmplitude = this.smoothedAmplitude;
            this.speakingMode = this.audioClient.isAgentSpeakingNow?.() ? 1 : 2;
        }
        const level = Math.min(1, this.currentAmplitude * 1.4);
        const agent = this.speakingMode === 1;
        const thinking = !agent && (this.thinkingFlag || !!this.canvas.closest('.cv-is-thinking'));
        const state: State = agent ? 'agent' : thinking ? 'thinking' : level > 0.08 ? 'user' : this.audioClient ? 'listening' : 'idle';

        // The floor passes over about half a second, as a person's attention would.
        const k = ease(0.45);
        const target = POSES[state];
        for (const key of Object.keys(target) as (keyof Pose)[]) this.pose[key] += (target[key] - this.pose[key]) * k;
        const { ink, paper } = this.palette[state];
        this.ink = this.ink.map((v, i) => v + (ink[i] - v) * k) as RGB;
        this.paper = this.paper.map((v, i) => v + (paper[i] - v) * k) as RGB;
        this.inVol += ((agent ? 0 : level) - this.inVol) * ease(0.12);
        this.outVol += ((agent ? level : 0) - this.outVol) * ease(0.12);
        // The stains move at the pace of whoever is speaking: the eased level of that voice.
        this.flow += (this.pose.flow + this.pose.voice * (agent ? this.outVol : this.inVol)) * dt;
        // The breath runs on the wall clock, so every orb on the page breathes in step.
        const breath = 0.5 + 0.5 * Math.sin((wall / 1000 / BREATH_SECONDS) * Math.PI * 2);

        // Keep the drawing buffer the size the canvas is shown at, whatever the window does: an orb
        // drawn into a buffer of another shape is squashed, and looks moved next to the live one.
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const bw = Math.round(this.canvas.clientWidth * dpr);
        const bh = Math.round(this.canvas.clientHeight * dpr);
        if (bw > 0 && bh > 0 && (bw !== this.canvas.width || bh !== this.canvas.height)) {
            this.canvas.width = bw;
            this.canvas.height = bh;
        }

        const u = this.uniforms;
        gl.viewport(0, 0, this.canvas.width, this.canvas.height);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(this.program);
        gl.uniform2f(u.uRes, this.canvas.width, this.canvas.height);
        gl.uniform1f(u.uFlow, this.flow);
        gl.uniform1f(u.uDepth, this.pose.depth);
        gl.uniform1f(u.uGain, this.pose.gain);
        gl.uniform1f(u.uContrast, this.pose.contrast);
        gl.uniform1f(u.uIn, this.inVol);
        gl.uniform1f(u.uOut, this.outVol);
        gl.uniform1f(u.uBreath, breath);
        gl.uniform1f(u.uOff, this.isDeactivated ? 1 : 0);
        gl.uniform1f(u.uCells, this.cells);
        gl.uniform1f(u.uLevels, LEVELS);
        gl.uniform3fv(u.uInk, this.ink);
        gl.uniform3fv(u.uPaper, this.paper);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
        this.animationId = requestAnimationFrame(this.render);
    };

    /** Follow a call's voices; null goes back to idling. */
    setAudioClient(client: any) {
        this.audioClient = client;
        if (!client) {
            this.smoothedAmplitude = 0;
            this.currentAmplitude = 0;
            this.speakingMode = 2;
            this.thinkingFlag = false;
        }
    }

    setSpeakingMode(mode: number) {
        this.speakingMode = mode;
    }

    setThinking(thinking: boolean) {
        this.thinkingFlag = thinking;
    }

    setDeactivated(deactivated: boolean) {
        this.isDeactivated = deactivated;
    }

    setAmplitude(amplitude: number) {
        this.currentAmplitude = amplitude;
    }

    setAccentColor(r: number, g: number, b: number) {
        this.accent = [r, g, b];
        this.palette = orbInks(this.accent, this.lightStage);
    }

    /** Light or dark inks from the background it is drawn on: the first ancestor with a colour. Read at
     *  start(); call it again after that background changes. */
    refreshStage() {
        let el: HTMLElement | null = this.canvas;
        while (el) {
            const m = getComputedStyle(el).backgroundColor.match(/[\d.]+/g);
            if (m && (m.length < 4 || Number(m[3]) > 0.5)) {
                const [r, g, b] = m.map(Number);
                this.lightStage = (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.5;
                break;
            }
            el = el.parentElement;
        }
        this.palette = orbInks(this.accent, this.lightStage);
    }

    update(amplitude: number) {
        this.setAmplitude(amplitude);
    }

    start() {
        if (this.isActive) return;
        this.isActive = true;
        this.lastFrame = 0;
        this.refreshStage();
        // Straight to the resting inks the first time; after that it eases from where it was.
        if (!this.drawn) {
            this.ink = this.palette.idle.ink;
            this.paper = this.palette.idle.paper;
            this.drawn = true;
        }
        this.render();
    }

    stop() {
        this.isActive = false;
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
        if (this.gl) {
            this.gl.clearColor(0, 0, 0, 0);
            this.gl.clear(this.gl.COLOR_BUFFER_BIT);
        }
        this.smoothedAmplitude = 0;
        this.currentAmplitude = 0;
    }

    resize(width: number, height: number) {
        this.canvas.width = width;
        this.canvas.height = height;
    }
}
