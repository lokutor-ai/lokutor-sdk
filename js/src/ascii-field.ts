// A quiet field of ASCII glyphs drifting over its canvas: ConversationalPanel's optional texture
// (`texture: true`). Light glyphs only and slow motion, so it reads as texture, never as something to
// watch. It stops drawing when off screen, and draws one still frame when the visitor asks for
// reduced motion.

type RGB = [number, number, number];

export interface AsciiFieldOptions {
    color: RGB;
    /** Opacity of the densest glyph. */
    opacity: number;
    /** Glyph height in CSS pixels. */
    cell?: number;
    /** Multiplies the field's own pace. */
    speed?: number;
    /** Caps how dense a glyph the field reaches, 0..1: 0.4 keeps it to dots and colons. */
    density?: number;
}

// Dark to light, and no heavy glyphs: '#', '%' and '@' read as noise at this size.
const GLYPHS = ' .:-=+*';
const MONO = '"IBM Plex Mono", ui-monospace, SFMono-Regular, Menlo, Consolas, monospace';

const FRAGMENT = `
    precision highp float;
    uniform vec2 uRes, uCell;
    uniform float uTime, uGlyphs, uOpacity, uDensity;
    uniform vec3 uColor;
    uniform sampler2D uAtlas;

    float hash13(vec3 p) { p = fract(p * 0.1031); p += dot(p, p.zyx + 31.32); return fract((p.x + p.y) * p.z); }
    float vnoise(vec3 p) {
        vec3 i = floor(p), f = fract(p);
        f = f * f * (3.0 - 2.0 * f);
        return mix(mix(mix(hash13(i), hash13(i + vec3(1,0,0)), f.x),
                       mix(hash13(i + vec3(0,1,0)), hash13(i + vec3(1,1,0)), f.x), f.y),
                   mix(mix(hash13(i + vec3(0,0,1)), hash13(i + vec3(1,0,1)), f.x),
                       mix(hash13(i + vec3(0,1,1)), hash13(i + vec3(1,1,1)), f.x), f.y), f.z);
    }
    float fbm(vec3 p) { float s = 0.0, a = 0.5; for (int k = 0; k < 3; k++) { s += a * vnoise(p); p = p * 2.03 + 5.1; a *= 0.5; } return s / 0.875; }

    void main() {
        vec2 frag = gl_FragCoord.xy;
        vec2 cell = floor(frag / uCell);
        vec2 c = (cell + 0.5) * uCell;
        vec2 p = c / uRes.y;
        float t = uTime;
        float n = fbm(vec3(p * 2.2 + vec2(t * 0.02, -t * 0.015), t * 0.04));
        float d = smoothstep(0.35, 0.85, n) * uDensity;
        float gi = floor(d * (uGlyphs - 1.0) + 0.5);
        vec2 local = fract(frag / uCell);
        float g = texture2D(uAtlas, vec2((gi + local.x) / uGlyphs, 1.0 - local.y)).a;
        float alpha = g * uOpacity * (0.35 + 0.65 * d);
        gl_FragColor = vec4(uColor * alpha, alpha);
    }
`;

export class AsciiField {
    private canvas: HTMLCanvasElement;
    private opts: Required<AsciiFieldOptions>;
    private gl: WebGLRenderingContext | null = null;
    private program: WebGLProgram | null = null;
    private atlas: WebGLTexture | null = null;
    private u: Record<string, WebGLUniformLocation | null> = {};
    private cell: [number, number] = [8, 13];
    private raf = 0;
    private visible = false;
    private still = false;
    private start = performance.now();
    private observers: { io?: IntersectionObserver; ro?: ResizeObserver } = {};

    constructor(canvas: HTMLCanvasElement, options: AsciiFieldOptions) {
        this.canvas = canvas;
        this.opts = { cell: 13, speed: 1, density: 1, ...options };
        try { this.still = window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { /* older browsers */ }
        if (!this.init()) return;
        this.resize();
        this.observers.ro = new ResizeObserver(() => this.resize());
        this.observers.ro.observe(canvas);
        this.observers.io = new IntersectionObserver(([entry]) => {
            this.visible = entry.isIntersecting;
            if (this.visible) this.loop();
        });
        this.observers.io.observe(canvas);
        document.fonts?.load(`16px ${MONO}`).then(() => { this.buildAtlas(); this.draw(); }).catch(() => {});
    }

    private init(): boolean {
        const gl = this.canvas.getContext('webgl', { premultipliedAlpha: true, antialias: false, alpha: true });
        if (!gl) return false;
        this.gl = gl;
        const compile = (type: number, src: string) => {
            const s = gl.createShader(type)!;
            gl.shaderSource(s, src);
            gl.compileShader(s);
            if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) { console.error('AsciiField shader:', gl.getShaderInfoLog(s)); return null; }
            return s;
        };
        const vs = compile(gl.VERTEX_SHADER, 'attribute vec2 position; void main() { gl_Position = vec4(position, 0.0, 1.0); }');
        const fs = compile(gl.FRAGMENT_SHADER, FRAGMENT);
        if (!vs || !fs) return false;
        const program = gl.createProgram()!;
        gl.attachShader(program, vs);
        gl.attachShader(program, fs);
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return false;
        this.program = program;
        for (const n of ['uRes', 'uCell', 'uTime', 'uGlyphs', 'uOpacity', 'uDensity', 'uColor', 'uAtlas']) this.u[n] = gl.getUniformLocation(program, n);
        const buf = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, buf);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
        const loc = gl.getAttribLocation(program, 'position');
        gl.enableVertexAttribArray(loc);
        gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
        this.atlas = gl.createTexture();
        return true;
    }

    private dpr() {
        return Math.min(window.devicePixelRatio || 1, 2);
    }

    private resize() {
        const w = Math.max(1, Math.round(this.canvas.clientWidth * this.dpr()));
        const h = Math.max(1, Math.round(this.canvas.clientHeight * this.dpr()));
        if (w === this.canvas.width && h === this.canvas.height) return;
        this.canvas.width = w;
        this.canvas.height = h;
        this.buildAtlas();
        this.draw();
    }

    private buildAtlas() {
        const gl = this.gl;
        if (!gl || !this.atlas) return;
        const h = Math.max(8, Math.round(this.opts.cell * this.dpr()));
        const w = Math.max(5, Math.round(h * 0.6));
        this.cell = [w, h];
        const c = document.createElement('canvas');
        c.width = w * GLYPHS.length;
        c.height = h;
        const ctx = c.getContext('2d')!;
        ctx.fillStyle = '#fff';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.font = `400 ${Math.round(h * 0.8)}px ${MONO}`;
        for (let i = 0; i < GLYPHS.length; i++) ctx.fillText(GLYPHS[i], i * w + w / 2, h / 2 + 1);
        gl.bindTexture(gl.TEXTURE_2D, this.atlas);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, c);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    }

    private draw() {
        const gl = this.gl;
        if (!gl || !this.program) return;
        const t = this.still ? 12 : ((performance.now() - this.start) / 1000) * this.opts.speed;
        gl.viewport(0, 0, this.canvas.width, this.canvas.height);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.useProgram(this.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, this.atlas);
        gl.uniform1i(this.u.uAtlas, 0);
        gl.uniform2f(this.u.uRes, this.canvas.width, this.canvas.height);
        gl.uniform2f(this.u.uCell, this.cell[0], this.cell[1]);
        gl.uniform1f(this.u.uTime, t);
        gl.uniform1f(this.u.uGlyphs, GLYPHS.length);
        gl.uniform1f(this.u.uOpacity, this.opts.opacity);
        gl.uniform1f(this.u.uDensity, this.opts.density);
        gl.uniform3fv(this.u.uColor, this.opts.color);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }

    private loop = () => {
        cancelAnimationFrame(this.raf);
        if (!this.visible) return;
        this.draw();
        if (!this.still) this.raf = requestAnimationFrame(this.loop);
    };

    /** Change the glyphs' colour, opacity or density, e.g. after the background behind them changed. */
    setStyle(options: Partial<AsciiFieldOptions>) {
        this.opts = { ...this.opts, ...options };
        if (options.cell !== undefined) this.buildAtlas();
        this.draw();
    }

    destroy() {
        cancelAnimationFrame(this.raf);
        this.visible = false;
        this.observers.io?.disconnect();
        this.observers.ro?.disconnect();
    }
}
