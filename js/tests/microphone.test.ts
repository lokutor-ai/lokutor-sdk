import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { BrowserAudioManager, requestMicrophone, VoiceAgentClient, type AudioManager } from '../src/index';
import { microphoneMessage } from '../src/conversational-panel';

// The call-start path that failed on iPhones (2026-09-28): the microphone asked for only after the
// connection opened, refused by iOS with no prompt, and connect() left pending while the agent talked.

class FakeTrack {
  readyState: 'live' | 'ended' = 'live';
  stop() { this.readyState = 'ended'; }
}
class FakeStream {
  tracks = [new FakeTrack()];
  getAudioTracks() { return this.tracks; }
  getTracks() { return this.tracks; }
}
class FakeContext {
  static made: FakeContext[] = [];
  static resumeNeverSettles = false;
  state = 'suspended';
  sampleRate = 48000;
  closed = 0;
  destination = {};
  constructor() { FakeContext.made.push(this); }
  resume() {
    if (FakeContext.resumeNeverSettles) return new Promise<void>(() => {});
    this.state = 'running';
    return Promise.resolve();
  }
  close() { this.closed++; this.state = 'closed'; return Promise.resolve(); }
  createAnalyser() { return { fftSize: 0, disconnect() {}, frequencyBinCount: 128 }; }
  createMediaStreamSource() { return { connect() {}, disconnect() {} }; }
  createScriptProcessor() { return { connect() {}, disconnect() {}, onaudioprocess: null }; }
}

let gum: ReturnType<typeof vi.fn>;
let session: { type: string } | undefined;

beforeEach(() => {
  FakeContext.made = [];
  FakeContext.resumeNeverSettles = false;
  gum = vi.fn(async () => new FakeStream());
  session = { type: 'auto' };
  vi.stubGlobal('AudioContext', FakeContext);
  vi.stubGlobal('window', { AudioContext: FakeContext });
  vi.stubGlobal('navigator', { mediaDevices: { getUserMedia: gum }, audioSession: session });
});
afterEach(() => {
  vi.unstubAllGlobals();
});

describe('requestMicrophone', () => {
  it('asks synchronously, inside the gesture that called it', () => {
    const p = requestMicrophone();
    expect(gum).toHaveBeenCalledTimes(1); // before any await: still the tap
    return p;
  });

  it("takes the page's audio session out of playback mode, where iOS refuses capture", async () => {
    session!.type = 'playback';
    await requestMicrophone();
    expect(session!.type).toBe('auto');
  });

  it('leaves an audio session that already allows capture alone', async () => {
    session!.type = 'play-and-record';
    await requestMicrophone();
    expect(session!.type).toBe('play-and-record');
  });
});

describe('BrowserAudioManager with the microphone the page asked for', () => {
  it('uses that stream instead of asking again after the connection opens', async () => {
    const stream = new FakeStream();
    const m = new BrowserAudioManager({ microphone: Promise.resolve(stream as any) });
    await m.startMicrophone(() => {});
    expect(gum).not.toHaveBeenCalled();
    expect(m.isRecording()).toBe(true);
  });

  it("reports the request's refusal, and is not left 'listening'", async () => {
    const refused = Object.assign(new Error('denied'), { name: 'NotAllowedError' });
    const m = new BrowserAudioManager({ microphone: Promise.reject(refused) });
    await expect(m.startMicrophone(() => {})).rejects.toThrow('denied');
    expect(m.isRecording()).toBe(false);
  });

  it('asks again when the stream it was given has already been stopped', async () => {
    const stale = new FakeStream();
    stale.tracks[0].stop();
    const m = new BrowserAudioManager({ microphone: stale as any });
    await m.startMicrophone(() => {});
    expect(gum).toHaveBeenCalledTimes(1);
  });
});

describe('BrowserAudioManager audio context', () => {
  it('does not stall the call start on a resume() iOS never settles', async () => {
    FakeContext.resumeNeverSettles = true;
    const m = new BrowserAudioManager();
    const t = Date.now();
    await m.init();
    expect(Date.now() - t).toBeLessThan(2000);
  });

  it('closes its context on cleanup, and makes a fresh one if used again', async () => {
    const m = new BrowserAudioManager();
    await m.init();
    m.cleanup();
    expect(FakeContext.made[0].closed).toBe(1);
    await m.init();
    expect(FakeContext.made).toHaveLength(2);
  });
});

class FakeSocket {
  static all: FakeSocket[] = [];
  readyState = 0;
  binaryType = '';
  closed = 0;
  onopen: ((ev: Event) => unknown) | null = null;
  onmessage: ((ev: MessageEvent) => unknown) | null = null;
  onerror: ((ev: Event) => unknown) | null = null;
  onclose: ((ev: { code: number; reason: string; wasClean: boolean }) => unknown) | null = null;
  constructor(public url: string) { FakeSocket.all.push(this); }
  send() {}
  close() { this.closed++; this.readyState = 3; this.onclose?.({ code: 1005, reason: '', wasClean: true }); }
  open() { this.readyState = 1; this.onopen?.(new Event('open')); }
}

class RefusingMic implements AudioManager {
  async init() {}
  async startMicrophone() { throw Object.assign(new Error('Permission denied'), { name: 'NotAllowedError' }); }
  stopMicrophone() {}
  playAudio() {}
  stopPlayback() {}
  cleanup() {}
  isMicMuted() { return false; }
  setMuted() {}
  getAmplitude() { return 0; }
}

describe('VoiceAgentClient when the microphone cannot start', () => {
  beforeEach(() => {
    FakeSocket.all = [];
    vi.stubGlobal('WebSocket', Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 }));
  });

  it('fails the connect with the reason and closes the session, instead of hanging while the agent talks', async () => {
    const errors: any[] = [];
    const client = new VoiceAgentClient({ apiKey: 'k', prompt: 'p', onError: (e) => errors.push(e) } as any);
    const p = client.connect(new RefusingMic());
    await new Promise((r) => setTimeout(r, 0));
    FakeSocket.all[0].open();
    await expect(p).rejects.toMatchObject({ code: 'audio.microphone_unavailable', retryable: false });
    expect(FakeSocket.all[0].closed).toBe(1);
    expect(errors.map((e) => e.code)).toEqual(['audio.microphone_unavailable']);
    await new Promise((r) => setTimeout(r, 50));
    expect(FakeSocket.all).toHaveLength(1); // no reconnect into another session without a microphone
  });
});

describe('microphoneMessage', () => {
  it('names a refusal, a missing device, and anything else', () => {
    expect(microphoneMessage({ code: 'audio.microphone_unavailable', detail: 'NotAllowedError: Permission denied' }))
      .toMatch(/blocked/);
    expect(microphoneMessage(Object.assign(new Error('x'), { name: 'NotFoundError' }))).toMatch(/No microphone/);
    expect(microphoneMessage(new Error('timed out'))).toMatch(/could not start/);
  });
});
