import { describe, it, expect, vi, beforeEach } from 'vitest';
import { VoiceAgentClient, TTSClient } from '../src/client';
import { VoiceStyle, Language } from '../src/types';

// Mock global WebSocket
const mockWs = {
  send: vi.fn(),
  close: vi.fn(),
  binaryType: '',
  onopen: null as any,
  onmessage: null as any,
  onerror: null as any,
  onclose: null as any,
};

// @ts-ignore
global.WebSocket = vi.fn().mockImplementation(() => mockWs);

// Mock global fetch
const mockFetch = vi.fn();
// @ts-ignore
global.fetch = mockFetch;

describe('VoiceAgentClient', () => {
  let client: VoiceAgentClient;

  beforeEach(() => {
    vi.clearAllMocks();
    client = new VoiceAgentClient({
      apiKey: 'test-key',
      prompt: 'test-prompt',
      voice: VoiceStyle.F1,
      language: Language.ENGLISH,
    });
  });

  it('should initialize with correct values', () => {
    expect(client.prompt).toBe('test-prompt');
    expect(client.voice).toBe(VoiceStyle.F1);
    expect(client.language).toBe(Language.ENGLISH);
  });

  it('should create a WebSocket with correct URL and query params', async () => {
    client.connect().catch(() => { }); // Don't wait for resolve

    expect(global.WebSocket).toHaveBeenCalledWith(
      expect.stringContaining('api_key=test-key')
    );
  });

  it('should call callbacks on messages', () => {
    expect(client.onAudio).toBeDefined();
  });
});

describe('TTSClient', () => {
  let client: TTSClient;

  beforeEach(() => {
    vi.clearAllMocks();
    client = new TTSClient({
      apiKey: 'test-key',
    });
  });

  it('should create a WebSocket with correct URL and query params', async () => {
    client.synthesize({ text: 'test' }).catch(() => { });

    expect(global.WebSocket).toHaveBeenCalledWith(
      expect.stringContaining('api_key=test-key')
    );
  });
});

describe('VoiceAgentClient REST discovery', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetch.mockReset();
  });

  function mockResponse(body: any, status = 200) {
    mockFetch.mockResolvedValueOnce({
      ok: status === 200,
      status,
      json: () => Promise.resolve(body),
      text: () => Promise.resolve(''),
    });
  }

  it('fetchVoices returns voice list and converts wss to https', async () => {
    mockResponse({ voices: [{ id: 'F1', gender: 'female', languages: ['en'] }] });
    const voices = await VoiceAgentClient.fetchVoices('wss://api.lokutor.com/ws/agent');
    expect(mockFetch).toHaveBeenCalledWith(
      'https://api.lokutor.com/ws/agent/voices',
      expect.objectContaining({ headers: { Accept: 'application/json' } })
    );
    expect(voices).toHaveLength(1);
    expect(voices[0].id).toBe('F1');
  });

  it('fetchLanguages returns language list', async () => {
    mockResponse({ languages: [{ code: 'en', name: 'English' }] });
    const langs = await VoiceAgentClient.fetchLanguages();
    expect(langs[0].code).toBe('en');
  });

  it('fetchModels returns model list', async () => {
    mockResponse({ models: [{ name: 'versa-1.0', default: true }] });
    const models = await VoiceAgentClient.fetchModels();
    expect(models[0].name).toBe('versa-1.0');
  });

  it('fetchConfig returns server limits', async () => {
    mockResponse({ max_text_length: 5000, min_speed: 0.5, max_speed: 2, min_steps: 1, max_steps: 128, sample_rate: 44100, channels: 1 });
    const cfg = await VoiceAgentClient.fetchConfig();
    expect(cfg.max_text_length).toBe(5000);
    expect(cfg.sample_rate).toBe(44100);
  });

  it('fetchStatus returns runtime status', async () => {
    mockResponse({ status: 'ok', version: '1.1.0', ready: true, goroutines: 42, uptime_seconds: 120, mem_alloc_bytes: 1024, active_connections: 0, failed_requests: 0, runtime: 'go1.24', inference: 'cpu', timestamp: '2026-01-01T00:00:00Z' });
    const st = await VoiceAgentClient.fetchStatus();
    expect(st.ready).toBe(true);
    expect(st.version).toBe('1.1.0');
  });

  it('fetchHealth returns health status', async () => {
    mockResponse({ status: 'ok', version: '1.0.0', runtime: 'go1.24', inference: 'cpu', load: 0, timestamp: '2026-01-01T00:00:00Z' });
    const h = await VoiceAgentClient.fetchHealth();
    expect(h.status).toBe('ok');
  });

  it('throws LokutorError on HTTP error', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: false,
      status: 503,
      json: () => Promise.resolve({}),
      text: () => Promise.resolve('Service Unavailable'),
    });
    await expect(VoiceAgentClient.fetchHealth()).rejects.toThrow('HTTP 503');
  });

  it('throws LokutorError on network failure', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network error'));
    await expect(VoiceAgentClient.fetchHealth()).rejects.toThrow('Failed to fetch');
  });
});
