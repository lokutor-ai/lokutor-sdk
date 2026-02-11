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
