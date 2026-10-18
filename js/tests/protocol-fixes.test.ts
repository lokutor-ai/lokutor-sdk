import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { VoiceAgentClient, TTSClient } from '../src/index';

// Wire-protocol behaviour fixed in 1.6.0, each against what the server (lokutor_tts pkg/api) sends.
class FakeSocket {
  static all: FakeSocket[] = [];
  readyState = 0;
  binaryType = '';
  sent: string[] = [];
  onopen: ((ev: Event) => unknown) | null = null;
  onmessage: ((ev: MessageEvent) => unknown) | null = null;
  onerror: ((ev: Event) => unknown) | null = null;
  onclose: ((ev: { code: number; reason: string; wasClean: boolean }) => unknown) | null = null;
  constructor(public url: string) { FakeSocket.all.push(this); }
  send(d: unknown) { this.sent.push(String(d)); }
  close() { this.readyState = 3; this.onclose?.({ code: 1000, reason: '', wasClean: true }); }
  open() { this.readyState = 1; this.onopen?.(new Event('open')); }
  message(obj: unknown) { this.onmessage?.(new MessageEvent('message', { data: typeof obj === 'string' ? obj : JSON.stringify(obj) })); }
  types() { return this.sent.map((s) => { try { return JSON.parse(s).type; } catch { return s; } }); }
}

const tick = () => new Promise((r) => setTimeout(r, 0));

async function connected(extra: Record<string, unknown> = {}) {
  const client = new VoiceAgentClient({ apiKey: 'k', prompt: 'p', ...extra } as any);
  const p = client.connect();
  await tick();
  await tick();
  FakeSocket.all.at(-1)!.open();
  await p;
  return client;
}

describe('protocol fixes', () => {
  const realWS = (globalThis as any).WebSocket;
  beforeEach(() => {
    FakeSocket.all = [];
    (globalThis as any).WebSocket = Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 });
  });
  afterEach(() => {
    (globalThis as any).WebSocket = realWS;
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('emits each tool call once, with the call_id submitToolResult needs', async () => {
    const client = await connected();
    const calls: any[] = [];
    client.on('tool_call', (c: any) => calls.push(c));
    FakeSocket.all[0].message({ type: 'tool_call', name: 'book', arguments: '{"day":"mon"}', call_id: 'c1' });
    expect(calls).toEqual([{ name: 'book', arguments: '{"day":"mon"}', call_id: 'c1' }]);
  });

  it('calls each callback once, and keeps partial transcripts out of the history', async () => {
    const onTranscription = vi.fn();
    const onStatus = vi.fn();
    const client = await connected({ onTranscription, onStatus });
    const partials: string[] = [];
    client.on('transcription_partial', (t: string) => partials.push(t));
    const ws = FakeSocket.all[0];
    ws.message({ type: 'transcript', role: 'user', data: 'hel', partial: true });
    ws.message({ type: 'transcript', role: 'user', data: 'hello there' });
    ws.message({ type: 'status', data: 'thinking', generation: 1 });
    expect(partials).toEqual(['hel']);
    expect(onTranscription).toHaveBeenCalledTimes(1);
    expect(onTranscription).toHaveBeenCalledWith('hello there');
    expect(onStatus.mock.calls.filter((c) => c[0] === 'thinking')).toHaveLength(1);
    expect(client.getTranscript().map((m) => m.text)).toEqual(['hello there']);
  });

  it('runs a dashboard agent without overwriting its voice, language or prompt', async () => {
    await connected({ agentId: 'a b', prompt: undefined });
    const ws = FakeSocket.all[0];
    expect(ws.url).toContain('agent_id=a%20b');
    expect(ws.types()).not.toContain('voice');
    expect(ws.types()).not.toContain('language');
    expect(ws.types()).not.toContain('prompt');
    expect(ws.types().at(-1)).toBe('rates'); // still releases the server's first-turn gate
  });

  it('sends an explicit voice for a dashboard agent when asked to', async () => {
    await connected({ agentId: 'a1', voice: 'M3', prompt: undefined });
    expect(FakeSocket.all[0].types()).toContain('voice');
  });

  it('asks the auth callback for a credential on every connect, URL-encoded', async () => {
    const auth = vi.fn().mockResolvedValue('wst_a+b/c');
    await connected({ apiKey: undefined, auth });
    expect(auth).toHaveBeenCalledTimes(1);
    expect(FakeSocket.all[0].url).toContain('api_key=wst_a%2Bb%2Fc');
  });

  it('TTS rejects on a server error frame instead of resolving', async () => {
    const client = new TTSClient({ apiKey: 'k' });
    const p = client.synthesize({ text: 'hi' });
    await tick();
    const ws = FakeSocket.all[0];
    ws.open();
    ws.message({ type: 'error', data: { code: 'validation.invalid_voice', message: 'Voice "X" is not recognised.', retryable: false } });
    await expect(p).rejects.toMatchObject({ code: 'validation.invalid_voice', retryable: false });
  });

  it('TTS sends speed and steps only when set', async () => {
    const client = new TTSClient({ apiKey: 'k' });
    const p = client.synthesize({ text: 'hi' });
    await tick();
    const ws = FakeSocket.all[0];
    ws.open();
    const req = JSON.parse(ws.sent[0]);
    expect(req.speed).toBeUndefined();
    expect(req.steps).toBeUndefined();
    ws.message('EOS');
    await p;
  });

  it('discovery helpers call the API root, not the agent socket path', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({ voices: [] }), text: () => Promise.resolve('') });
    (globalThis as any).fetch = fetchMock;
    await VoiceAgentClient.fetchVoices();
    expect(fetchMock.mock.calls[0][0]).toBe('https://api.lokutor.com/voices');
    await VoiceAgentClient.fetchHealth();
    expect(fetchMock.mock.calls[1][0]).toBe('https://api.lokutor.com/ready');
  });
});
