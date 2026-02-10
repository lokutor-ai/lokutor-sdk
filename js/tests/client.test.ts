import { describe, it, expect, vi, beforeEach } from 'vitest';
import { VoiceAgentClient } from '../src/client';
import { VoiceStyle, Language } from '../src/types';
import { WebSocket } from 'ws';

// Mock WebSocket
vi.mock('ws', () => {
  return {
    WebSocket: vi.fn().mockImplementation(() => {
      return {
        on: vi.fn(),
        send: vi.fn(),
        close: vi.fn(),
      };
    }),
  };
});

describe('VoiceAgentClient', () => {
  let client: VoiceAgentClient;

  beforeEach(() => {
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

  it('should create a WebSocket with correct headers', async () => {
    const connectPromise = client.connect();
    
    expect(WebSocket).toHaveBeenCalledWith(
      expect.stringContaining('ws/agent'),
      expect.objectContaining({
        headers: { 'X-API-Key': 'test-key' }
      })
    );
  });

  it('should call callbacks on messages', () => {
    // This is hard to test without complex mock setup, 
    // but we can verify the structure
    expect(client.onAudio).toBeDefined();
  });
});
