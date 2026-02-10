# Lokutor JavaScript SDK

[![npm version](https://img.shields.io/npm/v/@lokutor/sdk.svg)](https://www.npmjs.com/package/@lokutor/sdk)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Official JavaScript/TypeScript SDK for [Lokutor AI](https://lokutor.com), providing high-performance, real-time Voice-to-Voice AI.

## Features

- ⚡ **Ultra-low latency**: Optimized WebSocket streaming for real-time interactions.
- 🗣️ **Conversational Intelligence**: Integrated STT, LLM, and TTS with VAD and barge-in support.
- 🌍 **Multi-lingual**: Support for English, Spanish, French, Portuguese, and Korean.
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

## Hardware Compatibility

This SDK is platform-agnostic and works in both **Node.js** and **Browser** environments. 

### For Node.js
We recommend using `node-record-lpcm16` for recording and `speaker` for playback. See the [examples](./examples) folder for a complete Node.js CLI implementation.

### For Browser
Use the [Web Audio API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Audio_API) to capture microphone input and play back the received buffers.

## Documentation

Full documentation is available at [docs.lokutor.com](https://docs.lokutor.com).

## License

MIT © [Lokutor AI](https://lokutor.com)
