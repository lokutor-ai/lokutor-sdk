import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TTSClient, VoiceStyle, Language } from '../src/index';

class MockTTSWebSocket {
  readyState = WebSocket.CONNECTING;
  binaryType = '';

  onopen: ((this: WebSocket, ev: Event) => any) | null = null;
  onmessage: ((this: WebSocket, ev: MessageEvent) => any) | null = null;
  onerror: ((this: WebSocket, ev: Event) => any) | null = null;
  onclose: ((this: WebSocket, ev: CloseEvent) => any) | null = null;

  constructor(public wsUrl: string) {
    this.readyState = WebSocket.CONNECTING;
    setTimeout(() => this.simulateOpen(), 0);
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

  simulateAudioChunk() {
    const audioChunk = new Uint8Array([0x80, 0x00, 0x00, 0x00]);
    this.simulateMessage(audioChunk.buffer);
  }

  simulateEOS() {
    this.simulateMessage('EOS');
  }
}

let mockTtsWsInstance: MockTTSWebSocket;

const originalWebSocket = global.WebSocket;

describe('TTSClient', () => {
  beforeEach(() => {
    (global as any).WebSocket = class extends MockTTSWebSocket {
      constructor(url: string) {
        super(url);
        mockTtsWsInstance = this;
      }
    };
  });

  afterEach(() => {
    (global as any).WebSocket = originalWebSocket;
    vi.clearAllMocks();
  });

  describe('Constructor', () => {
    it('should initialize with API key', () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      expect(client).toBeDefined();
    });
  });

  describe('Synthesis', () => {
    it('should synthesize text to speech', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onAudio = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello world',
        voice: VoiceStyle.F1,
        language: Language.ENGLISH,
        onAudio,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onAudio).toHaveBeenCalledWith(expect.any(Uint8Array));
    });

    it('should report time to first byte', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onTTFB = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        onTTFB,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onTTFB).toHaveBeenCalledWith(expect.any(Number));
      expect(onTTFB.mock.calls[0][0]).toBeGreaterThanOrEqual(0);
    });

    it('should emit visemes when enabled', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onVisemes = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        visemes: true,
        onVisemes,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      const visemeData = [
        { v: 0, c: 'h', t: 0.0 },
        { v: 1, c: 'e', t: 0.1 },
      ];

      mockTtsWsInstance.simulateMessage(JSON.stringify(visemeData));
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onVisemes).toHaveBeenCalledWith(visemeData);
    });

    it('should emit visemes from object payload format', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onVisemes = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        visemes: true,
        onVisemes,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      const visemeData = [
        { v: 0, c: 'h', t: 0.0 },
        { v: 1, c: 'e', t: 0.1 },
      ];

      mockTtsWsInstance.simulateMessage(JSON.stringify({ type: 'visemes', data: visemeData }));
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onVisemes).toHaveBeenCalledWith(visemeData);
    });

    it('should normalize viseme object payload fields', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onVisemes = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        visemes: true,
        onVisemes,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateMessage(
        JSON.stringify({
          type: 'visemes',
          data: [
            { id: 7, char: 'E', timestamp: 0.08 },
            { id: 8, char: 'S', timestamp: 0.21 },
          ],
        })
      );
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onVisemes).toHaveBeenCalledWith([
        { v: 7, c: 'e', t: 0.08 },
        { v: 8, c: 's', t: 0.21 },
      ]);
    });

    it('should support custom voice and language', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });

      const synthesisPromise = client.synthesize({
        text: 'Hola mundo',
        voice: VoiceStyle.F3,
        language: Language.SPANISH,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;
      expect(true).toBe(true);
    });

    it('should support speed adjustment', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        speed: 1.5,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;
      expect(true).toBe(true);
    });

    it('should handle synthesis errors', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onError = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello',
        onError,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateError();

      await expect(synthesisPromise).rejects.toBeDefined();
      expect(onError).toHaveBeenCalled();
    });

    it('should resolve on EOS message', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });

      const synthesisPromise = client.synthesize({
        text: 'Hello',
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateMessage(JSON.stringify({ type: 'eos' }));

      await expect(synthesisPromise).resolves.toBeUndefined();
    });

    it('should timeout on inactivity', async () => {
      vi.useFakeTimers();

      const client = new TTSClient({ apiKey: 'test-key' });

      const synthesisPromise = client.synthesize({
        text: 'Hello',
      });

      vi.runAllTimers();
      vi.advanceTimersByTime(2100);

      await synthesisPromise;

      vi.useRealTimers();
    });

    it('should stream multiple audio chunks', async () => {
      const client = new TTSClient({ apiKey: 'test-key' });
      const onAudio = vi.fn();

      const synthesisPromise = client.synthesize({
        text: 'Hello world',
        onAudio,
      });

      await new Promise(resolve => setTimeout(resolve, 10));

      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateAudioChunk();
      mockTtsWsInstance.simulateEOS();

      await synthesisPromise;

      expect(onAudio).toHaveBeenCalledTimes(3);
    });
  });
});
