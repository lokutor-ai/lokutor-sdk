import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { VoiceAgentClient, BrowserAudioManager, type AudioManager } from '../src/index';

// A socket the test drives by hand, including closes with a code and a reason (the server's view of
// why a session ended), which the shared mocks elsewhere cannot express.
class FakeSocket {
  static all: FakeSocket[] = [];
  readyState = 0;
  binaryType = '';
  sent: unknown[] = [];
  onopen: ((ev: Event) => unknown) | null = null;
  onmessage: ((ev: MessageEvent) => unknown) | null = null;
  onerror: ((ev: Event) => unknown) | null = null;
  onclose: ((ev: { code: number; reason: string; wasClean: boolean }) => unknown) | null = null;
  constructor(public url: string) { FakeSocket.all.push(this); }
  send(d: unknown) { this.sent.push(d); }
  close() { this.serverClose(1005, ''); }
  open() { this.readyState = 1; this.onopen?.(new Event('open')); }
  message(obj: unknown) { this.onmessage?.(new MessageEvent('message', { data: JSON.stringify(obj) })); }
  serverClose(code: number, reason: string) {
    this.readyState = 3;
    this.onclose?.({ code, reason, wasClean: code !== 1006 });
  }
}

class FakeAudio implements AudioManager {
  starts = 0;
  played: Uint8Array[] = [];
  async init() {}
  async startMicrophone() { this.starts++; }
  stopMicrophone() {}
  playAudio(d: Uint8Array) { this.played.push(d); }
  stopPlayback() {}
  cleanup() {}
  isMicMuted() { return false; }
  setMuted() {}
  getAmplitude() { return 0; }
}

const tick = () => new Promise((r) => setTimeout(r, 0));

async function connected(audio?: FakeAudio, extra: Record<string, unknown> = {}) {
  const client = new VoiceAgentClient({ apiKey: 'k', prompt: 'p', ...extra } as any);
  const p = client.connect(audio);
  await tick();
  FakeSocket.all.at(-1)!.open();
  await p;
  return client;
}

describe('Session lifecycle', () => {
  const realWS = (globalThis as any).WebSocket;
  beforeEach(() => {
    FakeSocket.all = [];
    (globalThis as any).WebSocket = Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 });
  });
  afterEach(() => {
    (globalThis as any).WebSocket = realWS;
    vi.useRealTimers();
  });

  it('does not reconnect when the server ends the session on purpose, and says why', async () => {
    const onError = vi.fn();
    const onStatus = vi.fn();
    const ended = vi.fn();
    const client = await connected(new FakeAudio(), { onError, onStatus });
    client.on('ended', ended);
    vi.useFakeTimers({ toFake: ['setTimeout'] });

    FakeSocket.all[0].serverClose(1008, 'Demo time limit reached. Create a free account to keep going.');
    vi.advanceTimersByTime(60_000);

    expect(FakeSocket.all).toHaveLength(1); // no second socket: no fresh session behind the caller's back
    expect(onError).toHaveBeenCalledTimes(1);
    const err = onError.mock.calls[0][0];
    expect(err.code).toBe('session.ended');
    expect(err.message).toBe('Demo time limit reached. Create a free account to keep going.');
    expect(err.retryable).toBe(false);
    expect(onStatus).toHaveBeenLastCalledWith('disconnected');
    expect(ended).toHaveBeenCalledWith({ code: 1008, reason: 'Demo time limit reached. Create a free account to keep going.' });
  });

  it('does not reconnect after a normal close (the agent ended the call)', async () => {
    await connected(new FakeAudio());
    vi.useFakeTimers({ toFake: ['setTimeout'] });
    FakeSocket.all[0].serverClose(1000, '');
    vi.advanceTimersByTime(60_000);
    expect(FakeSocket.all).toHaveLength(1);
  });

  it('reconnects after a dropped connection with one microphone and replies that play', async () => {
    const audio = new FakeAudio();
    const onStatus = vi.fn();
    await connected(audio, { onStatus });

    // The first session reaches generation 5.
    FakeSocket.all[0].message({ type: 'status', data: 'thinking', generation: 5 });
    FakeSocket.all[0].serverClose(1006, '');
    expect(onStatus).toHaveBeenCalledWith('reconnecting');

    await new Promise((r) => setTimeout(r, 2100)); // first backoff is 2 s
    expect(FakeSocket.all).toHaveLength(2);
    FakeSocket.all[1].open();
    await tick();

    expect(audio.starts).toBe(1); // the running microphone is reused, not doubled

    // The new session numbers its replies from 1: they must play, not be discarded as stale.
    FakeSocket.all[1].message({ type: 'audio', data: btoa('\u0001\u0002'), generation: 1 });
    expect(audio.played).toHaveLength(1);
  }, 10_000);

  it('announces a session limit sent by the server', async () => {
    const client = await connected(new FakeAudio());
    const limit = vi.fn();
    client.on('session_limit', limit);
    FakeSocket.all[0].message({ type: 'session_limit', data: { seconds: 180, reason: 'demo' } });
    expect(limit).toHaveBeenCalledWith({ seconds: 180, reason: 'demo' });
  });
});

describe('BrowserAudioManager microphone and screen', () => {
  const saved: Record<string, any> = {};
  let lockReleased = 0;
  let lockRequests = 0;
  let getUserMediaCalls = 0;

  beforeEach(() => {
    lockReleased = 0; lockRequests = 0; getUserMediaCalls = 0;
    for (const k of ['window', 'document', 'navigator', 'AudioContext']) saved[k] = (globalThis as any)[k];
    const node = () => ({ connect() {}, disconnect() {}, onaudioprocess: null });
    class FakeCtx {
      state = 'running'; sampleRate = 16000; currentTime = 0; destination = {};
      createAnalyser() { return { ...node(), fftSize: 0, frequencyBinCount: 8 }; }
      createMediaStreamSource() { return node(); }
      createScriptProcessor() { return node(); }
      resume() { return Promise.resolve(); }
    }
    (globalThis as any).AudioContext = FakeCtx;
    (globalThis as any).window = { AudioContext: FakeCtx };
    (globalThis as any).document = { visibilityState: 'visible', addEventListener() {}, removeEventListener() {} };
    Object.defineProperty(globalThis, 'navigator', {
      configurable: true,
      value: {
        mediaDevices: { getUserMedia: async () => { getUserMediaCalls++; return { getTracks: () => [{ stop() {} }], getAudioTracks: () => [] }; } },
        wakeLock: { request: async () => { lockRequests++; return { release: async () => { lockReleased++; }, addEventListener() {} }; } },
      },
    });
  });
  afterEach(() => {
    for (const k of ['window', 'document', 'AudioContext']) (globalThis as any)[k] = saved[k];
    Object.defineProperty(globalThis, 'navigator', { configurable: true, value: saved.navigator });
  });

  it('starting the microphone twice keeps one capture pipeline', async () => {
    const m = new BrowserAudioManager();
    await m.startMicrophone(() => {});
    await m.startMicrophone(() => {});
    expect(getUserMediaCalls).toBe(1);
  });

  it('keeps the screen awake while the microphone is live, and lets it sleep after', async () => {
    const m = new BrowserAudioManager();
    await m.startMicrophone(() => {});
    await tick();
    expect(lockRequests).toBe(1);
    m.stopMicrophone();
    await tick();
    expect(lockReleased).toBe(1);
  });

  it('does not hold the screen when keepAwake is false', async () => {
    const m = new BrowserAudioManager({ keepAwake: false });
    await m.startMicrophone(() => {});
    await tick();
    expect(lockRequests).toBe(0);
  });
});
