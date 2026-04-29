import { TTSClient, VoiceStyle, Language } from '../dist/index.mjs';

const apiKey = process.env.LOKUTOR_API_KEY;

if (!apiKey) {
  console.error('LOKUTOR_API_KEY is missing');
  process.exit(1);
}

const client = new TTSClient({ apiKey });

let audioChunks = 0;
let audioBytes = 0;
let visemeBatches = 0;
let visemeItems = 0;
let ttfb = -1;
const visemeChars = new Set();

await client.synthesize({
  text: 'Hola, esta es una validacion de audio y visemas del SDK de Lokutor para comprobar que el streaming funciona correctamente y que los visemas llegan con tiempos utiles para sincronizacion.',
  voice: VoiceStyle.F1,
  language: Language.SPANISH,
  visemes: true,
  onAudio: (chunk) => {
    audioChunks += 1;
    audioBytes += chunk.length;
  },
  onVisemes: (payload) => {
    visemeBatches += 1;
    if (Array.isArray(payload)) {
      visemeItems += payload.length;
      for (const item of payload) {
        if (item && item.c) {
          visemeChars.add(String(item.c).toLowerCase());
        }
      }
    }
  },
  onTTFB: (ms) => {
    ttfb = ms;
  },
  onError: (err) => {
    console.error('TTS error:', err);
  },
});

const result = {
  audioChunks,
  audioBytes,
  visemeBatches,
  visemeItems,
  uniqueVisemes: [...visemeChars].sort(),
  ttfb,
};

console.log('LIVE_SMOKE_RESULT=' + JSON.stringify(result));

if (audioChunks === 0 || audioBytes === 0) {
  console.error('Audio validation failed');
  process.exit(2);
}

if (visemeBatches === 0 || visemeItems === 0) {
  console.error('Viseme validation failed');
  process.exit(3);
}
