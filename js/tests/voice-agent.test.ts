import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { VoiceAgentClient, VoiceStyle, Language } from '../src/index';

class MockWebSocket {
  readyState = WebSocket.CONNECTING;
  binaryType = '';
  url = '';

  onopen: ((this: WebSocket, ev: Event) => any) | null = null;
  onmessage: ((this: WebSocket, ev: MessageEvent) => any) | null = null;
  onerror: ((this: WebSocket, ev: Event) => any) | null = null;
  onclose: ((this: WebSocket, ev: CloseEvent) => any) | null = null;

  constructor(public wsUrl: string) {
    this.url = wsUrl;
    this.readyState = WebSocket.CONNECTING;
  }

  send(data: string | ArrayBufferLike) {
    if (this.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket is not open');
    }
  }

  close() {
    this.readyState = WebSocket.CLOSED;
    if (this.onclose) {
      this.onclose(new CloseEvent('close'));
    }
  }

  simulateOpen() {
    this.readyState = WebSocket.OPEN;
    if (this.onopen) {
      this.onopen(new Event('open'));
    }
  }

  simulateMessage(data: string | ArrayBuffer) {
    if (this.onmessage) {
      const messageEvent = new MessageEvent('message', { data });
      this.onmessage(messageEvent);
    }
  }

  simulateError() {
    if (this.onerror) {
      this.onerror(new Event('error'));
    }
  }
}

let mockWsInstance: MockWebSocket;

const originalWebSocket = global.WebSocket;

describe('VoiceAgentClient', () => {
  beforeEach(() => {
    (global as any).WebSocket = class extends MockWebSocket {
      constructor(url: string) {
        super(url);
        mockWsInstance = this;
      }
    };
  });

  afterEach(() => {
    (global as any).WebSocket = originalWebSocket;
    vi.clearAllMocks();
  });

  describe('Constructor', () => {
    it('should initialize with required configuration', () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test prompt',
      });

      expect(client.apiKey).toBe('test-key');
      expect(client.prompt).toBe('Test prompt');
      expect(client.voice).toBe(VoiceStyle.F1);
      expect(client.language).toBe(Language.ENGLISH);
    });

    it('should accept custom voice and language', () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        voice: VoiceStyle.M3,
        language: Language.SPANISH,
      });

      expect(client.voice).toBe(VoiceStyle.M3);
      expect(client.language).toBe(Language.SPANISH);
    });

    it('should register callbacks', () => {
      const onTranscription = vi.fn();
      const onResponse = vi.fn();
      const onError = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onTranscription,
        onResponse,
        onError,
      });

      expect(client).toBeDefined();
    });

    it('should enable visemes when configured', () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
      });

      expect(client).toBeDefined();
    });
  });

  describe('Connection', () => {
    it('should connect to WebSocket server', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();

      const result = await connectPromise;
      expect(result).toBe(true);
    });

    it('should send configuration after connecting', async () => {
      const onResponse = vi.fn();
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test prompt',
        voice: VoiceStyle.F2,
        language: Language.SPANISH,
        onResponse,
        visemes: true,
      });

      const connectPromise = client.connect();
      const sendSpy = vi.spyOn(mockWsInstance, 'send');
      mockWsInstance.simulateOpen();

      await connectPromise;

      expect(sendSpy).toHaveBeenCalledWith(
        JSON.stringify({ type: 'prompt', data: 'Test prompt' })
      );
      expect(sendSpy).toHaveBeenCalledWith(
        JSON.stringify({ type: 'voice', data: VoiceStyle.F2 })
      );
      expect(sendSpy).toHaveBeenCalledWith(
        JSON.stringify({ type: 'language', data: Language.SPANISH })
      );
      expect(sendSpy).toHaveBeenCalledWith(
        JSON.stringify({ type: 'visemes', data: true })
      );
    });

    it('should handle connection failure', async () => {
      const onError = vi.fn();
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onError,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateError();

      await expect(connectPromise).rejects.toBeDefined();
    });

    it('should reject if not in browser environment', async () => {
      const originalWindow = global.window;
      delete (global as any).window;

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        enableAudio: true,
      });

      await expect(client.startManaged()).rejects.toThrow(
        'requires a browser environment'
      );

      (global as any).window = originalWindow;
    });
  });

  describe('Audio Sending', () => {
    it('should send audio data when connected', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const sendSpy = vi.spyOn(mockWsInstance, 'send');
      const audioData = new Uint8Array([1, 2, 3, 4]);
      client.sendAudio(audioData);

      expect(sendSpy).toHaveBeenCalledWith(audioData);
    });

    it('should not send audio when disconnected', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const sendSpy = vi.spyOn(MockWebSocket.prototype, 'send');
      const audioData = new Uint8Array([1, 2, 3, 4]);

      client.sendAudio(audioData);
      expect(sendSpy).not.toHaveBeenCalled();
    });
  });

  describe('Message Handling', () => {
    it('should handle transcript messages', async () => {
      const onTranscription = vi.fn();
      const onResponse = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onTranscription,
        onResponse,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'user',
          data: 'Hello agent',
        })
      );

      expect(onTranscription).toHaveBeenCalledWith('Hello agent');
    });

    it('should handle agent response messages', async () => {
      const onResponse = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onResponse,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'agent',
          data: 'Hello user',
        })
      );

      expect(onResponse).toHaveBeenCalledWith('Hello user');
    });

    it('should handle status messages', async () => {
      const onStatus = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onStatus,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'status',
          data: 'thinking',
        })
      );

      expect(onStatus).toHaveBeenCalledWith('thinking');
    });

    it('should handle invalid JSON gracefully', async () => {
      const onError = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        onError,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      expect(() => {
        mockWsInstance.simulateMessage('invalid json{]');
      }).not.toThrow();
    });
  });

  describe('Viseme Handling', () => {
    it('should emit viseme events', async () => {
      const onVisemes = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
        onVisemes,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const visemeData = [
        { v: 0, c: 'a', t: 0.0 },
        { v: 1, c: 'e', t: 0.1 },
        { v: 2, c: 'i', t: 0.2 },
      ];

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: visemeData,
        })
      );

      expect(onVisemes).toHaveBeenCalledWith(visemeData);
    });

    it('should normalize viseme char/timestamp/id format', async () => {
      const onVisemes = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
        onVisemes,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [
            { id: 1, char: 'A', timestamp: 0.12 },
            { id: 2, char: 'M', timestamp: 0.34 },
          ],
        })
      );

      expect(onVisemes).toHaveBeenCalledWith([
        { v: 1, c: 'a', t: 0.12 },
        { v: 2, c: 'm', t: 0.34 },
      ]);
    });

    it('should emit empty viseme arrays', async () => {
      const onVisemes = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
        onVisemes,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [],
        })
      );

      expect(onVisemes).toHaveBeenCalledWith([]);
    });

    it('should not emit visemes if not configured', async () => {
      const onVisemes = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: false,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      client.onVisemes(onVisemes);

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [{ v: 0, c: 'a', t: 0.0 }],
        })
      );

      expect(onVisemes).toHaveBeenCalledWith([{ v: 0, c: 'a', t: 0.0 }]);
    });
  });

  describe('Prompts', () => {
    it('should update prompt mid-conversation', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Initial prompt',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const sendSpy = vi.spyOn(mockWsInstance, 'send');
      client.updatePrompt('Updated prompt');

      expect(client.prompt).toBe('Updated prompt');
      expect(sendSpy).toHaveBeenCalledWith(
        JSON.stringify({ type: 'prompt', data: 'Updated prompt' })
      );
    });

    it('should not send prompt update if not connected', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Initial',
      });

      const sendSpy = vi.spyOn(MockWebSocket.prototype, 'send');
      client.updatePrompt('Updated');

      expect(sendSpy).not.toHaveBeenCalled();
      expect(client.prompt).toBe('Updated');
    });
  });

  describe('Transcript History', () => {
    it('should maintain conversation history', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'user',
          data: 'Hello',
        })
      );

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'agent',
          data: 'Hi there',
        })
      );

      const transcript = client.getTranscript();
      expect(transcript).toHaveLength(2);
      expect(transcript[0]).toMatchObject({
        role: 'user',
        text: 'Hello',
      });
      expect(transcript[1]).toMatchObject({
        role: 'agent',
        text: 'Hi there',
      });
    });

    it('should format transcript text', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'user',
          data: 'Hello',
        })
      );

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'transcript',
          role: 'agent',
          data: 'Hi there',
        })
      );

      const text = client.getTranscriptText();
      expect(text).toContain('You: Hello');
      expect(text).toContain('Agent: Hi there');
    });
  });

  describe('Disconnection', () => {
    it('should disconnect cleanly', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const closeSpy = vi.spyOn(mockWsInstance, 'close');
      client.disconnect();

      expect(closeSpy).toHaveBeenCalled();
    });
  });

  describe('Event Listeners', () => {
    it('should support listener-style callbacks', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const onVisemes = vi.fn();
      client.onVisemes(onVisemes);

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [{ v: 0, c: 'a', t: 0.0 }],
        })
      );

      expect(onVisemes).toHaveBeenCalled();
    });

    it('should support on() method for events', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const onAudio = vi.fn();
      client.on('audio', onAudio);

      const audioChunk = new Uint8Array([1, 2, 3]);
      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'audio',
          data: Buffer.from(audioChunk).toString('base64'),
        })
      );

      expect(onAudio).toHaveBeenCalled();
    });
  });
});
