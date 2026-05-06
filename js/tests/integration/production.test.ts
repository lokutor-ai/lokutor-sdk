import { describe, it, expect } from 'vitest';
import { TTSClient, VoiceAgentClient, VoiceStyle, Language } from '../../src/index';

const apiKey = process.env.LOKUTOR_API_KEY;

function skipIfNoKey() {
  if (!apiKey) {
    console.log('SKIP: Set LOKUTOR_API_KEY to run integration tests against the real server');
    return true;
  }
  return false;
}

describe('Integration: Production Server', () => {
  describe('Discovery (no auth)', () => {
    it('fetchVoices returns real voice data', async () => {
      if (skipIfNoKey()) return;

      const voices = await VoiceAgentClient.fetchVoices();
      expect(voices.length).toBeGreaterThan(0);
      expect(voices[0]).toHaveProperty('id');
      expect(voices[0]).toHaveProperty('gender');
      console.log('Voices:', voices.map(v => v.id));
    }, 10000);

    it('fetchConfig returns server limits', async () => {
      if (skipIfNoKey()) return;

      const config = await VoiceAgentClient.fetchConfig();
      expect(config.max_text_length).toBeGreaterThan(0);
      expect(config.sample_rate).toBe(44100);
      expect(config.channels).toBe(1);
      console.log('Config:', config);
    }, 10000);

    it('fetchHealth returns ok or warming_up', async () => {
      if (skipIfNoKey()) return;

      const health = await VoiceAgentClient.fetchHealth();
      expect(['ok', 'warming_up']).toContain(health.status);
      console.log('Health:', health.status);
    }, 10000);
  });

  describe('TTS (auth required)', () => {
    it('synthesizes audio and receives PCM chunks', async () => {
      if (skipIfNoKey()) return;

      const client = new TTSClient({ apiKey });
      let chunkCount = 0;
      let totalBytes = 0;
      let receivedTTFB = -1;

      await client.synthesize({
        text: 'Hello, this is a test.',
        voice: VoiceStyle.F1,
        language: Language.ENGLISH,
        steps: 5,
        onAudio: (chunk) => {
          chunkCount++;
          totalBytes += chunk.length;
          // Verify PCM16: chunk length should be even (2 bytes per sample)
          expect(chunk.length % 2).toBe(0);
        },
        onTTFB: (ms) => {
          receivedTTFB = ms;
        },
      });

      expect(chunkCount).toBeGreaterThan(0);
      expect(totalBytes).toBeGreaterThan(0);
      expect(receivedTTFB).toBeGreaterThanOrEqual(0);
      console.log(`TTS: ${chunkCount} chunks, ${totalBytes} bytes, TTFB: ${receivedTTFB}ms`);
    }, 15000);

    it('synthesizes with visemes and receives viseme data', async () => {
      if (skipIfNoKey()) return;

      const client = new TTSClient({ apiKey });
      let visemeCount = 0;
      const chars = new Set<string>();

      await client.synthesize({
        text: 'Hello world',
        voice: VoiceStyle.F1,
        visemes: true,
        onVisemes: (visemes) => {
          expect(Array.isArray(visemes)).toBe(true);
          for (const v of visemes) {
            expect(v).toHaveProperty('v');
            expect(v).toHaveProperty('c');
            expect(v).toHaveProperty('t');
            expect(typeof v.t).toBe('number');
            expect(v.t).toBeGreaterThanOrEqual(0);
            chars.add(v.c);
          }
          visemeCount += visemes.length;
        },
      });

      expect(visemeCount).toBeGreaterThan(0);
      console.log(`Visemes: ${visemeCount} total, chars: ${[...chars].sort().join(',')}`);
    }, 15000);
  });

  describe('Voice Agent (auth required)', () => {
    it('connects and sends config messages', async () => {
      if (skipIfNoKey()) return;

      const client = new VoiceAgentClient({
        apiKey,
        prompt: 'You are a test assistant. Reply with exactly: Connected.',
        voice: VoiceStyle.F1,
        language: Language.ENGLISH,
      });

      const connected = await client.connect();
      expect(connected).toBe(true);

      // Give server time to process
      await new Promise(r => setTimeout(r, 500));

      client.disconnect();
      console.log('VoiceAgent: connected and disconnected cleanly');
    }, 15000);
  });
});
