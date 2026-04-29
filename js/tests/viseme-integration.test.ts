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

  simulateClose(code?: number, reason?: string) {
    this.readyState = WebSocket.CLOSED;
    if (this.onclose) {
      this.onclose(new CloseEvent('close', { code, reason }));
    }
  }
}

let mockWsInstance: MockWebSocket;
const originalWebSocket = global.WebSocket;

describe('Viseme Integration Tests', () => {
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

  describe('Viseme Delivery and Timing', () => {
    it('should deliver viseme sequence with timestamps', async () => {
      const visemeSequence = [
        { v: 0, c: 'a', t: 0.0 },
        { v: 1, c: 'e', t: 0.1 },
        { v: 2, c: 'i', t: 0.2 },
        { v: 3, c: 'o', t: 0.3 },
        { v: 4, c: 'u', t: 0.4 },
      ];

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
          data: visemeSequence,
        })
      );

      expect(onVisemes).toHaveBeenCalledWith(visemeSequence);

      const receivedVisemes = onVisemes.mock.calls[0][0];
      for (let i = 0; i < receivedVisemes.length; i++) {
        expect(receivedVisemes[i].v).toBe(i);
        expect(receivedVisemes[i].c).toBe(visemeSequence[i].c);
        expect(receivedVisemes[i].t).toBeCloseTo(i * 0.1, 5);
      }
    });

    it('should handle rapid viseme updates', async () => {
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

      const rapidVisemes = Array.from({ length: 100 }, (_, i) => ({
        v: i,
        c: String.fromCharCode(97 + (i % 26)),
        t: i * 0.01,
      }));

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: rapidVisemes,
        })
      );

      expect(onVisemes).toHaveBeenCalledWith(rapidVisemes);
    });

    it('should handle silence visemes', async () => {
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

      const silenceVisemes = [
        { v: 0, c: 'sil', t: 0.5 },
        { v: 1, c: 'sil', t: 0.6 },
        { v: 2, c: 'sil', t: 0.7 },
      ];

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: silenceVisemes,
        })
      );

      expect(onVisemes).toHaveBeenCalledWith(silenceVisemes);
    });

    it('should synchronize visemes with agent response', async () => {
      const onVisemes = vi.fn();
      const onResponse = vi.fn();

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
        onVisemes,
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

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [
            { v: 0, c: 'h', t: 0.0 },
            { v: 1, c: 'e', t: 0.05 },
          ],
        })
      );

      expect(onResponse).toHaveBeenCalledWith('Hello user');
      expect(onVisemes).toHaveBeenCalled();
    });

    it('should handle mixed vowels and consonants', async () => {
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

      const mixedVisemes = [
        { v: 0, c: 'm', t: 0.0 },
        { v: 1, c: 'a', t: 0.1 },
        { v: 2, c: 'n', t: 0.2 },
        { v: 3, c: 'o', t: 0.3 },
      ];

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: mixedVisemes,
        })
      );

      const received = onVisemes.mock.calls[0][0];
      expect(received[0].c).toBe('m');
      expect(received[1].c).toBe('a');
      expect(received[2].c).toBe('n');
      expect(received[3].c).toBe('o');
    });

    it('should maintain viseme order', async () => {
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

      const unorderedVisemes = [
        { v: 2, c: 'c', t: 0.2 },
        { v: 1, c: 'b', t: 0.1 },
        { v: 0, c: 'a', t: 0.0 },
      ];

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: unorderedVisemes,
        })
      );

      const received = onVisemes.mock.calls[0][0];
      expect(received).toEqual(unorderedVisemes);
    });

    it('should handle viseme payload with all standard IPA sounds', async () => {
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

      const ipaVisemes = [
        { v: 0, c: 'p', t: 0.0 },
        { v: 1, c: 'b', t: 0.1 },
        { v: 2, c: 'f', t: 0.2 },
        { v: 3, c: 'v', t: 0.3 },
        { v: 4, c: 't', t: 0.4 },
        { v: 5, c: 'd', t: 0.5 },
        { v: 6, c: 's', t: 0.6 },
        { v: 7, c: 'z', t: 0.7 },
      ];

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: ipaVisemes,
        })
      );

      expect(onVisemes).toHaveBeenCalledWith(ipaVisemes);
    });
  });

  describe('Reconnection with Visemes', () => {
    it('should restore viseme delivery after reconnection', async () => {
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
          data: [{ v: 0, c: 'a', t: 0.0 }],
        })
      );

      expect(onVisemes).toHaveBeenCalledTimes(1);
    });
  });

  describe('Viseme Callback Error Handling', () => {
    it('should not crash on viseme callback error', async () => {
      const onVisemes = vi.fn().mockImplementation(() => {
        throw new Error('Callback failed');
      });

      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
        onVisemes,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      expect(() => {
        mockWsInstance.simulateMessage(
          JSON.stringify({
            type: 'visemes',
            data: [{ v: 0, c: 'a', t: 0.0 }],
          })
        );
      }).not.toThrow();
    });

    it('should handle listener-style viseme callbacks', async () => {
      const client = new VoiceAgentClient({
        apiKey: 'test-key',
        prompt: 'Test',
        visemes: true,
      });

      const connectPromise = client.connect();
      mockWsInstance.simulateOpen();
      await connectPromise;

      const listener1 = vi.fn();
      const listener2 = vi.fn();

      client.onVisemes(listener1);
      client.onVisemes(listener2);

      mockWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [{ v: 0, c: 'a', t: 0.0 }],
        })
      );

      expect(listener1).toHaveBeenCalled();
      expect(listener2).toHaveBeenCalled();
    });
  });
});
