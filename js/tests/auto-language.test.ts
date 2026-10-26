import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { VoiceAgentClient, Language, AUTO_LANGUAGE, languageToWire } from '../src/index';

// language: omitted keeps the default; '' or 'auto' goes out as data:"" so the server follows the caller.
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
  languages() {
    return this.sent.map((s) => JSON.parse(s)).filter((m) => m.type === 'language').map((m) => m.data);
  }
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

describe('language: auto', () => {
  const realWS = (globalThis as any).WebSocket;
  beforeEach(() => {
    FakeSocket.all = [];
    (globalThis as any).WebSocket = Object.assign(FakeSocket, { OPEN: 1, CONNECTING: 0, CLOSED: 3 });
  });
  afterEach(() => { (globalThis as any).WebSocket = realWS; });

  it("omitted keeps today's default: en", async () => {
    const { client, ws } = await connected();
    expect(client.language).toBe(Language.ENGLISH);
    expect(ws.languages()).toEqual(['en']);
    client.disconnect();
  });

  it.each([['auto'], ['']])('%j sends data:""', async (value) => {
    const { client, ws } = await connected({ language: value });
    expect(client.language).toBe('');
    expect(ws.languages()).toEqual(['']);
    client.disconnect();
  });

  it('an agentId session sends nothing when omitted, "" when auto', async () => {
    const omitted = await connected({ agentId: 'a1' });
    expect(omitted.ws.languages()).toEqual([]);
    omitted.client.disconnect();
    const auto = await connected({ agentId: 'a1', language: 'auto' });
    expect(auto.ws.languages()).toEqual(['']);
    auto.client.disconnect();
  });

  it('a named language is unchanged', async () => {
    const { client, ws } = await connected({ language: Language.CATALAN });
    expect(ws.languages()).toEqual(['ca']);
    client.disconnect();
  });

  it('updateLanguage accepts auto mid-call', async () => {
    const { client, ws } = await connected({ language: 'es' });
    client.updateLanguage(AUTO_LANGUAGE);
    expect(ws.languages()).toEqual(['es', '']);
    client.disconnect();
  });

  it('languageToWire (what the panel passes on): auto and "" -> "", the rest unchanged', () => {
    expect(languageToWire('auto')).toBe('');
    expect(languageToWire('')).toBe('');
    expect(languageToWire(Language.GERMAN)).toBe('de');
  });
});
