import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { VoiceAgentClient, TTSClient } from '../../src/client';
import { VoiceStyle, Language, LokutorError, isRetryable } from '../../src/types';

const mockWs = {
  send: vi.fn(),
  close: vi.fn(),
  binaryType: '',
  readyState: 1,
  onopen: null as any,
  onmessage: null as any,
  onerror: null as any,
  onclose: null as any,
};

// @ts-ignore
global.WebSocket = vi.fn().mockImplementation(() => mockWs);
global.WebSocket.OPEN = 1;
global.WebSocket.CLOSED = 3;

const mockFetch = vi.fn();
// @ts-ignore
global.fetch = mockFetch;

describe('Docs: Getting Started', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetch.mockReset();
  });

  it('discovery call works without auth', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: () => Promise.resolve({ voices: [{ id: 'F1', gender: 'female', languages: ['en'] }] }),
    });

    const voices = await VoiceAgentClient.fetchVoices();
    expect(voices).toHaveLength(1);
    expect(voices[0].id).toBe('F1');
  });

  it('TTS client can be created with API key', () => {
    const client = new TTSClient({ apiKey: 'sk-test-123' });
    expect(client).toBeDefined();
  });

  it('VoiceAgentClient can be created with API key and prompt', () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test-123',
      prompt: 'You are helpful.',
      voice: VoiceStyle.F1,
      language: Language.ENGLISH,
    });
    expect(client.prompt).toBe('You are helpful.');
    expect(client.voice).toBe(VoiceStyle.F1);
  });
});

describe('Docs: SDK Discovery', () => {
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

  it('fetchVoices returns voice list', async () => {
    mockResponse({ voices: [{ id: 'F1', gender: 'female', languages: ['en', 'es'] }] });
    const voices = await VoiceAgentClient.fetchVoices();
    expect(voices[0].id).toBe('F1');
    expect(voices[0].gender).toBe('female');
  });

  it('fetchConfig returns server limits', async () => {
    mockResponse({
      max_text_length: 5000,
      min_speed: 0.5,
      max_speed: 2.0,
      min_steps: 1,
      max_steps: 128,
      sample_rate: 44100,
      channels: 1,
    });
    const cfg = await VoiceAgentClient.fetchConfig();
    expect(cfg.max_text_length).toBe(5000);
    expect(cfg.sample_rate).toBe(44100);
  });

  it('validateRequest helper logic works', async () => {
    mockResponse({
      max_text_length: 5000,
      min_speed: 0.5,
      max_speed: 2.0,
      min_steps: 1,
      max_steps: 128,
      sample_rate: 44100,
      channels: 1,
    });
    const config = await VoiceAgentClient.fetchConfig();

    function validateRequest(text: string, speed: number, steps: number) {
      if (text.length > config.max_text_length) {
        throw new Error(`Text too long. Max: ${config.max_text_length}`);
      }
      if (speed < config.min_speed || speed > config.max_speed) {
        throw new Error(`Speed must be ${config.min_speed}-${config.max_speed}`);
      }
      if (steps < config.min_steps || steps > config.max_steps) {
        throw new Error(`Steps must be ${config.min_steps}-${config.max_steps}`);
      }
    }

    expect(() => validateRequest('x'.repeat(5001), 1, 5)).toThrow('Text too long');
    expect(() => validateRequest('hello', 3, 5)).toThrow('Speed must be');
    expect(() => validateRequest('hello', 1, 200)).toThrow('Steps must be');
    expect(() => validateRequest('hello', 1, 5)).not.toThrow();
  });

  it('fetchHealth returns status', async () => {
    mockResponse({ status: 'ok', version: '1.0.0', runtime: 'go1.24', inference: 'cpu', load: 0, timestamp: '2026-01-01T00:00:00Z' });
    const health = await VoiceAgentClient.fetchHealth();
    expect(health.status).toBe('ok');
  });

  it('fetchStatus returns runtime info', async () => {
    mockResponse({ status: 'ok', version: '1.1.0', ready: true, goroutines: 42, uptime_seconds: 120, mem_alloc_bytes: 1024, active_connections: 0, failed_requests: 0, runtime: 'go1.24', inference: 'cpu', timestamp: '2026-01-01T00:00:00Z' });
    const status = await VoiceAgentClient.fetchStatus();
    expect(status.ready).toBe(true);
    expect(status.uptime_seconds).toBe(120);
  });

  it('isRetryable handles discovery errors', async () => {
    mockFetch.mockRejectedValueOnce(new Error('Network error'));
    try {
      await VoiceAgentClient.fetchConfig();
    } catch (err) {
      expect(isRetryable(err)).toBe(true);
    }
  });
});

describe('Docs: SDK TTS', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('TTSClient synthesize opens WebSocket', async () => {
    const client = new TTSClient({ apiKey: 'sk-test' });
    const synthPromise = client.synthesize({ text: 'Hello world', voice: VoiceStyle.F1 });

    // Simulate WebSocket open and EOS
    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) mockWs.onmessage({ data: 'EOS' });
    }, 10);

    await synthPromise;
    expect(global.WebSocket).toHaveBeenCalledWith(expect.stringContaining('api_key=sk-test'));
    expect(mockWs.send).toHaveBeenCalled();
  });

  it('TTS synthesize options are accepted', async () => {
    const client = new TTSClient({ apiKey: 'sk-test' });
    const synthPromise = client.synthesize({
      text: 'Hello',
      voice: VoiceStyle.M2,
      language: Language.SPANISH,
      speed: 1.2,
      steps: 10,
      visemes: true,
      onAudio: vi.fn(),
      onVisemes: vi.fn(),
      onTTFB: vi.fn(),
      onError: vi.fn(),
    });

    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) mockWs.onmessage({ data: 'EOS' });
    }, 10);

    await synthPromise;
    expect(mockWs.send).toHaveBeenCalled();
  });

  it('error handling with isRetryable', async () => {
    const client = new TTSClient({ apiKey: 'sk-test' });
    const onError = vi.fn();

    const synthPromise = client.synthesize({
      text: 'Hello',
      onError,
    });

    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      const err = new Error('WebSocket error');
      if (mockWs.onerror) mockWs.onerror(err);
      if (mockWs.onclose) mockWs.onclose({ code: 1011 });
    }, 10);

    try { await synthPromise; } catch { /* expected */ }
    expect(onError).toHaveBeenCalled();
  });
});

describe('Docs: SDK Voice Agent', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('VoiceAgentClient initializes with all options', () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
      voice: VoiceStyle.F1,
      language: Language.ENGLISH,
      visemes: true,
    });

    expect(client.prompt).toBe('You are helpful.');
    expect(client.voice).toBe(VoiceStyle.F1);
    expect(client.language).toBe(Language.ENGLISH);
  });

  it('event subscription works', async () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    const onTranscription = vi.fn();
    const onResponse = vi.fn();
    const onStatus = vi.fn();
    const onError = vi.fn();

    client.on('transcription', onTranscription);
    client.on('response', onResponse);
    client.on('status', onStatus);
    client.on('error', onError);

    const connectPromise = client.connect();

    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) {
        mockWs.onmessage({ data: JSON.stringify({ type: 'transcript', role: 'user', data: 'Hello' }) });
        mockWs.onmessage({ data: JSON.stringify({ type: 'transcript', role: 'agent', data: 'Hi there!' }) });
        mockWs.onmessage({ data: JSON.stringify({ type: 'status', data: 'listening' }) });
      }
    }, 10);

    await connectPromise;

    expect(onTranscription).toHaveBeenCalledWith('Hello');
    expect(onResponse).toHaveBeenCalledWith('Hi there!');
    expect(onStatus).toHaveBeenCalledWith('listening');
  });

  it('status lifecycle icons mapping works', async () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    const icons: Record<string, string> = {
      listening: '👂',
      thinking: '🧠',
      speaking: '🔊',
      interrupted: '⚡',
    };

    client.on('status', (status: string) => {
      const icon = icons[status] || status;
      expect(icon).toBeDefined();
    });

    const connectPromise = client.connect();
    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) {
        mockWs.onmessage({ data: JSON.stringify({ type: 'status', data: 'listening' }) });
      }
    }, 10);

    await connectPromise;
  });

  it('interruption handling clears state', async () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    let audioQueueCleared = false;
    let visemeTimelineCleared = false;

    client.on('status', (status: string) => {
      if (status === 'interrupted') {
        audioQueueCleared = true;
        visemeTimelineCleared = true;
      }
    });

    const connectPromise = client.connect();
    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) {
        mockWs.onmessage({ data: JSON.stringify({ type: 'status', data: 'interrupted' }) });
      }
    }, 10);

    await connectPromise;
    expect(audioQueueCleared).toBe(true);
    expect(visemeTimelineCleared).toBe(true);
  });

  it('transcript access works', async () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    const connectPromise = client.connect();
    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
      if (mockWs.onmessage) {
        mockWs.onmessage({ data: JSON.stringify({ type: 'transcript', role: 'user', data: 'Hello' }) });
        mockWs.onmessage({ data: JSON.stringify({ type: 'response', data: 'Hi!' }) });
      }
    }, 10);

    await connectPromise;

    const history = client.getTranscript();
    expect(history.length).toBeGreaterThan(0);

    const text = client.getTranscriptText();
    expect(typeof text).toBe('string');
  });

  it('updatePrompt sends prompt message', async () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    const connectPromise = client.connect();
    setTimeout(() => {
      if (mockWs.onopen) mockWs.onopen();
    }, 10);

    await connectPromise;
    client.updatePrompt('You are now a pirate.');
    expect(mockWs.send).toHaveBeenCalledWith(expect.stringContaining('pirate'));
  });

  it('toggleMute and getAmplitude exist', () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You are helpful.',
    });

    expect(typeof client.toggleMute).toBe('function');
    expect(typeof client.getAmplitude).toBe('function');
  });
});

describe('Docs: SDK Visemes', () => {
  it('mouth shape mapping covers all characters', () => {
    const mouthShapes: Record<string, string> = {
      'a': 'open-wide',
      'e': 'open-medium',
      'i': 'open-medium',
      'o': 'open-round',
      'u': 'open-round',
      'm': 'lips-together',
      'p': 'lips-together',
      'b': 'lips-together',
      'f': 'lip-bite',
      'v': 'lip-bite',
      't': 'teeth-close',
      'd': 'teeth-close',
      's': 'teeth-close',
      'z': 'teeth-close',
      'l': 'tongue-up',
      'n': 'small',
      'r': 'small',
      'k': 'back-throat',
      'g': 'back-throat',
      'sil': 'closed',
    };

    const allChars = ['a', 'e', 'i', 'o', 'u', 'm', 'p', 'b', 'f', 'v', 't', 'd', 's', 'z', 'l', 'n', 'r', 'k', 'g', 'sil'];
    for (const c of allChars) {
      expect(mouthShapes[c]).toBeDefined();
      expect(typeof mouthShapes[c]).toBe('string');
    }
  });

  it('viseme timeline sorting works', () => {
    interface Viseme { v: number; c: string; t: number; }

    const visemeTimeline: Viseme[] = [];
    const visemes: Viseme[] = [
      { v: 2, c: 'a', t: 0.1 },
      { v: 0, c: 'h', t: 0.0 },
      { v: 1, c: 'sil', t: 0.05 },
    ];

    visemeTimeline.push(...visemes);
    visemeTimeline.sort((a, b) => a.t - b.t);

    expect(visemeTimeline[0].t).toBe(0.0);
    expect(visemeTimeline[1].t).toBe(0.05);
    expect(visemeTimeline[2].t).toBe(0.1);
  });

  it('active viseme lookup works', () => {
    interface Viseme { v: number; c: string; t: number; }

    const timeline: Viseme[] = [
      { v: 0, c: 'h', t: 0.0 },
      { v: 1, c: 'sil', t: 0.05 },
      { v: 2, c: 'a', t: 0.1 },
    ];

    function getActiveViseme(now: number): Viseme | undefined {
      let active = timeline[0];
      for (const v of timeline) {
        if (v.t <= now) active = v;
        else break;
      }
      return active;
    }

    expect(getActiveViseme(0.03)?.c).toBe('h');
    expect(getActiveViseme(0.07)?.c).toBe('sil');
    expect(getActiveViseme(0.15)?.c).toBe('a');
  });

  it('lerp function works correctly', () => {
    function lerp(start: number, end: number, t: number) {
      return start + (end - start) * t;
    }

    expect(lerp(0, 100, 0)).toBe(0);
    expect(lerp(0, 100, 1)).toBe(100);
    expect(lerp(0, 100, 0.5)).toBe(50);
    expect(lerp(10, 20, 0.15)).toBe(11.5);
  });

  it('LipSyncAnimator class pattern compiles and runs', () => {
    interface Viseme { v: number; c: string; t: number; }

    const mouthShapes: Record<string, string> = {
      'a': 'open-wide',
      'e': 'open-medium',
      'sil': 'closed',
    };

    class LipSyncAnimator {
      private timeline: Viseme[] = [];
      private activeShape = 'closed';

      addVisemes(visemes: Viseme[]) {
        this.timeline.push(...visemes);
        this.timeline.sort((a, b) => a.t - b.t);
      }

      clear() {
        this.timeline = [];
        this.activeShape = 'closed';
      }

      getActiveShapeAtTime(now: number): string {
        let active = this.timeline[0];
        for (const v of this.timeline) {
          if (v.t <= now) active = v;
          else break;
        }
        return mouthShapes[active?.c || 'sil'] || 'closed';
      }
    }

    const animator = new LipSyncAnimator();
    animator.addVisemes([
      { v: 0, c: 'a', t: 0.0 },
      { v: 1, c: 'sil', t: 0.1 },
    ]);

    expect(animator.getActiveShapeAtTime(0.05)).toBe('open-wide');
    animator.clear();
    expect(animator.getActiveShapeAtTime(0.05)).toBe('closed');
  });
});

describe('Docs: SDK Tool Calling', () => {
  it('tool definition schema is valid', () => {
    const tools = [
      {
        type: 'function',
        function: {
          name: 'get_weather',
          description: 'Get the current weather for a city',
          parameters: {
            type: 'object',
            properties: {
              city: { type: 'string', description: 'The city name' }
            },
            required: ['city']
          }
        }
      }
    ];

    expect(tools[0].type).toBe('function');
    expect(tools[0].function.name).toBe('get_weather');
    expect(tools[0].function.parameters.required).toContain('city');
  });

  it('VoiceAgentClient accepts tools in constructor', () => {
    const client = new VoiceAgentClient({
      apiKey: 'sk-test',
      prompt: 'You can check the weather.',
      tools: [
        {
          type: 'function',
          function: {
            name: 'get_weather',
            description: 'Get weather',
            parameters: { type: 'object', properties: {}, required: [] }
          }
        }
      ],
    });

    expect(client).toBeDefined();
  });
});

describe('Docs: Error Handling', () => {
  it('isRetryable returns true for retryable errors', () => {
    const retryableErr = new LokutorError('tts.synthesis_failed', 'Failed', undefined, true);
    expect(isRetryable(retryableErr)).toBe(true);
  });

  it('isRetryable returns false for fatal errors', () => {
    const fatalErr = new LokutorError('auth.invalid_key', 'Invalid key', undefined, false);
    expect(isRetryable(fatalErr)).toBe(false);
  });

  it('isRetryable returns false for non-LokutorError', () => {
    expect(isRetryable(new Error('random'))).toBe(false);
  });
});

describe('Docs: Version History', () => {
  it('SDK version is 1.1.19', () => {
    // This test documents the current version in docs
    // If version changes, update both docs and this test
    const pkg = require('../../package.json');
    expect(pkg.version).toBe('1.1.19');
  });
});
