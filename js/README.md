# Lokutor JavaScript SDK

![Lokutor Hero](../../assets/img/hero.png)

[![npm version](https://img.shields.io/npm/v/@lokutor/sdk.svg)](https://www.npmjs.com/package/@lokutor/sdk)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Official JavaScript/TypeScript SDK for [Lokutor AI](https://lokutor.com), providing high-performance, real-time Voice-to-Voice AI.

## Features

- ⚡ **Ultra-low latency**: Optimized WebSocket streaming for real-time interactions.
- 🗣️ **Conversational Intelligence**: Integrated STT, LLM, and TTS with VAD and barge-in support.
- 🌍 **Multi-lingual**: English, Spanish, Catalan, Galician, Basque, Portuguese, French, Italian and German (Catalan, Galician and Basque are speak-only: no speech recognition yet).
- 🎨 **Natural Voices**: Multiple high-quality male and female voice styles.
- 💻 **TypeScript First**: Full type definitions included.

## Installation

```bash
npm install @lokutor/sdk
# or
yarn add @lokutor/sdk
```

## Quick Start

### 1. Voice Agent (Conversational AI)

The `VoiceAgentClient` handles everything from audio streaming to conversational logic.

```typescript
import { VoiceAgentClient, VoiceStyle, Language } from '@lokutor/sdk';

const client = new VoiceAgentClient({
  apiKey: 'your-api-key',
  prompt: 'You are a helpful and friendly AI assistant.',
  onTranscription: (text) => console.log('User:', text),
  onResponse: (text) => console.log('AI:', text)
});

await client.connect();
// Feed PCM audio data to the client:
// client.sendAudio(pcmData);
```

### 2. Standalone Streaming TTS

Use `TTSClient` to convert text into high-quality audio streams without the conversational overhead.

```typescript
import { TTSClient, VoiceStyle } from '@lokutor/sdk';

const client = new TTSClient({ apiKey: 'your-api-key' });

await client.synthesize({
  text: 'Hello world, this is a test of Lokutor streaming TTS.',
  voice: VoiceStyle.F1,
  onAudio: (buffer) => {
    // Play the audio buffer (Node.js or Browser)
    console.log(`Received ${buffer.length} bytes of audio`);
  }
});
```

## Using the SDK in a browser without exposing your key

Never ship an API key in browser code. Mint a short-lived ticket on your server and let the SDK fetch a
fresh one before every connect (tickets last 10 minutes, so reconnects need a new one):

```js
// Your server (Node 22+, key stays here)
app.post('/lokutor-ticket', async (_req, res) => {
  const r = await fetch('https://api.lokutor.com/ws-ticket', {
    method: 'POST',
    headers: { Authorization: `Bearer ${process.env.LOKUTOR_API_KEY}` },
  });
  res.json(await r.json()); // { ticket: "wst_...", expires_in: 600 }
});

// The page
const agent = new VoiceAgentClient({
  auth: async () => (await (await fetch('/lokutor-ticket', { method: 'POST' })).json()).ticket,
  prompt: 'You are a helpful assistant.',
  enableAudio: true,
});
await agent.connect();
```

To run an agent you configured in the dashboard, pass `agentId` and leave out `prompt`, `voice` and
`language`: only what you set explicitly overrides the agent.

## Hardware Compatibility

This SDK is platform-agnostic and works in both **Node.js** (22 or newer, for the global `WebSocket`) and **Browser** environments. 

### For Node.js
We recommend using `node-record-lpcm16` for recording and `speaker` for playback. See the [examples](./examples) folder for a complete Node.js CLI implementation.

### For Browser
Use the [Web Audio API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API) to capture microphone input and play back the received buffers.

## Documentation

Full documentation is available at [docs.lokutor.com](https://docs.lokutor.com).

## License

MIT © [Lokutor AI](https://lokutor.com)
