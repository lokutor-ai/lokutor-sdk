import { describe, it, expect, vi, beforeEach } from 'vitest';
import { STTClient, SpeechToTextClient } from '../src/client';
import { Language } from '../src/types';

// Custom vocabulary reaches the API as it takes it: a list in JSON and in /ws/stt's first message, one
// comma-separated field in a file upload; nothing is sent when there are none.

const mockFetch = vi.fn();
// @ts-ignore
global.fetch = mockFetch;

const sent: string[] = [];
const ws = { send: (d: string) => sent.push(d), close: vi.fn(), readyState: 1, binaryType: '', onopen: null as any, onmessage: null as any, onerror: null as any, onclose: null as any };
// @ts-ignore
global.WebSocket = vi.fn().mockImplementation(() => ws);

const ok = () => ({ ok: true, status: 200, json: async () => ({ text: 'hola', latency_ms: 1, engine: 'lokutor-stt', sample_rate: 16000, language: 'es', duration_seconds: 1 }) });

describe('keyterms', () => {
  beforeEach(() => { mockFetch.mockReset(); sent.length = 0; });

  it('goes in the JSON body for raw PCM', async () => {
    mockFetch.mockResolvedValue(ok());
    await new STTClient({ apiKey: 'k' }).transcribe({ audio: new Uint8Array(3200), format: 'pcm16', sampleRate: 16000, language: Language.SPANISH, keyterms: ['Etxeberria', 'Invisalign'] });
    const body = JSON.parse(mockFetch.mock.calls[0][1].body);
    expect(body.keyterms).toEqual(['Etxeberria', 'Invisalign']);
  });

  it('is a comma-separated form field for a file upload, and absent when empty', async () => {
    mockFetch.mockResolvedValue(ok());
    const client = new STTClient({ apiKey: 'k' });
    await client.transcribe({ audio: new Blob([new Uint8Array(44)]), language: Language.SPANISH, keyterms: ['Etxeberria', 'Clínica Sol'] });
    expect((mockFetch.mock.calls[0][1].body as FormData).get('keyterms')).toBe('Etxeberria,Clínica Sol');
    await client.transcribe({ audio: new Blob([new Uint8Array(44)]), language: Language.SPANISH, keyterms: [] });
    expect((mockFetch.mock.calls[1][1].body as FormData).get('keyterms')).toBeNull();
  });

  it('is in /ws/stt\'s first message', async () => {
    const stt = new SpeechToTextClient({ apiKey: 'k', language: Language.SPANISH, keyterms: ['Etxeberria'] });
    const audio = { init: async () => {}, startMicrophone: async () => {}, stopMicrophone: () => {}, cleanup: () => {} };
    const p = stt.connect(audio as any).catch(() => {});
    await new Promise(r => setTimeout(r, 0)); // connect() awaits the audio manager before opening the socket
    await ws.onopen?.();
    const first = JSON.parse(sent[0]);
    expect(first).toMatchObject({ language: 'es', keyterms: ['Etxeberria'] });
    ws.onmessage?.({ data: JSON.stringify({ type: 'status', data: 'ready' }) });
    await Promise.race([p, new Promise(r => setTimeout(r, 10))]);
  });
});
