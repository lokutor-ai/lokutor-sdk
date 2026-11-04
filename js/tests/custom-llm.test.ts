import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { VoiceAgentClient } from '../src/index';

// llm: your own OpenAI-compatible endpoint for the session, sent before the first turn is released ("rates").
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
  messages() { return this.sent.map((s) => JSON.parse(s)); }
}
const tick = () => new Promise((r) => setTimeout(r, 0));

async function connected(extra: Record<string, unknown> = {}) {
  const client = new VoiceAgentClient({ apiKey: 'k', prompt: 'p', ...extra } as any);
  const p = client.connect();
  await tick();
  await tick();
  const ws = FakeSocket.all.at(-1)!;
  ws.open();
  await p;
  return { client, ws };
}

describe('llm: your own language model', () => {
  const realWS = (globalThis as any).WebSocket;
  beforeEach(() => {
    FakeSocket.all = [];
    (globalThis as any).WebSocket = Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 });
  });
  afterEach(() => { (globalThis as any).WebSocket = realWS; });

  it('is sent as an llm message in the endpoint\'s snake_case, before rates', async () => {
    const { client, ws } = await connected({
      llm: { baseUrl: 'https://agent.example.com/v1', apiKey: 'sk-1', model: 'my-agent', headers: { 'X-Tenant': 'acme' }, tools: 'none' },
    });
    const msgs = ws.messages();
    const types = msgs.map((m) => m.type);
    expect(types.indexOf('llm')).toBeGreaterThan(-1);
    expect(types.indexOf('llm')).toBeLessThan(types.indexOf('rates'));
    expect(msgs[types.indexOf('llm')].data).toMatchObject({
      base_url: 'https://agent.example.com/v1', api_key: 'sk-1', model: 'my-agent', headers: { 'X-Tenant': 'acme' }, tools: 'none',
    });
    client.disconnect();
  });

  it('is not sent unless asked for', async () => {
    const { client, ws } = await connected();
    expect(ws.messages().map((m) => m.type)).not.toContain('llm');
    client.disconnect();
  });
});

describe('onLlmRequest: your model runs in this process', () => {
  const realWS = (globalThis as any).WebSocket;
  beforeEach(() => {
    FakeSocket.all = [];
    (globalThis as any).WebSocket = Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 });
  });
  afterEach(() => { (globalThis as any).WebSocket = realWS; });

  const request = (ws: FakeSocket, extra: Record<string, unknown> = {}) =>
    ws.onmessage?.({ data: JSON.stringify({ type: 'llm_request', request_id: 'r1', messages: [{ role: 'user', content: 'hi' }], ...extra }) } as MessageEvent);
  const waitFor = async (ws: FakeSocket, type: string) => {
    for (let i = 0; i < 100; i++) { if (ws.messages().some((m) => m.type === type)) return; await new Promise((r) => setTimeout(r, 5)); }
  };

  it('asks for client mode and streams an async generator back', async () => {
    const seen: any[] = [];
    const { client, ws } = await connected({
      onLlmRequest: async function* (messages: any[]) { seen.push(messages); yield 'Hola, '; yield 'Ana.'; yield { name: 'end_call' }; },
    });
    expect(ws.messages().find((m) => m.type === 'llm')!.data).toEqual({ mode: 'client', tools: 'auto' });
    request(ws);
    await waitFor(ws, 'llm_done');
    const out = ws.messages().filter((m) => m.request_id === 'r1');
    expect(out.map((m) => m.type)).toEqual(['llm_chunk', 'llm_chunk', 'llm_tool_call', 'llm_done']);
    expect(out[2]).toMatchObject({ name: 'end_call', arguments: '{}' });
    expect(seen[0][0].content).toBe('hi');
    client.disconnect();
  });

  it('a plain string reply, and an error is reported instead of hanging', async () => {
    const a = await connected({ onLlmRequest: () => 'listo' });
    request(a.ws);
    await waitFor(a.ws, 'llm_done');
    expect(a.ws.messages().filter((m) => m.request_id === 'r1').map((m) => m.type)).toEqual(['llm_chunk', 'llm_done']);
    a.client.disconnect();
    const b = await connected({ onLlmRequest: () => { throw new Error('graph exploded'); } });
    request(b.ws);
    await waitFor(b.ws, 'llm_error');
    expect(b.ws.messages().find((m) => m.type === 'llm_error')!.data).toContain('graph exploded');
    b.client.disconnect();
  });

  it('llm_cancel aborts the signal and stops the stream', async () => {
    let aborted = false;
    const { client, ws } = await connected({
      onLlmRequest: async function* (_m: any[], _t: any[], signal: AbortSignal) {
        signal.addEventListener('abort', () => { aborted = true; });
        for (let i = 0; i < 50; i++) { await new Promise((r) => setTimeout(r, 10)); yield `w${i} `; }
      },
    });
    request(ws);
    await new Promise((r) => setTimeout(r, 40));
    ws.onmessage?.({ data: JSON.stringify({ type: 'llm_cancel', request_id: 'r1' }) } as MessageEvent);
    await new Promise((r) => setTimeout(r, 60));
    expect(aborted).toBe(true);
    expect(ws.messages().some((m) => m.type === 'llm_done')).toBe(false);
    client.disconnect();
  });
});
