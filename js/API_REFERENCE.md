# API Reference - Lokutor JS SDK

## `VoiceAgentClient`

The main class for interacting with the Lokutor Voice Agent.

### Constructor

```typescript
new VoiceAgentClient(config: LokutorConfig & { 
    prompt: string, 
    voice?: VoiceStyle, 
    language?: Language 
})
```

**Config Options:**
- `apiKey` (string): Your Lokutor API Key.
- `prompt` (string): The system prompt defining the AI's persona.
- `voice` (VoiceStyle): Optional. Default is `VoiceStyle.F1`.
- `language` (Language): Optional. Default is `Language.ENGLISH`.
- `onTranscription` (function): Callback for user speech transcriptions.
- `onResponse` (function): Callback for AI text responses.
- `onAudio` (function): Callback for raw agent audio buffers.
- `onStatus` (function): Callback for session status changes (interrupted, thinking, speaking, listening).
- `onError` (function): Callback for error events.

### Methods

#### `connect(): Promise<boolean>`
Establishes a connection to the Lokutor server. Resolved when the connection is open and configured.

#### `sendAudio(audioData: Buffer | Uint8Array)`
Sends raw PCM audio data (16-bit, 16kHz, mono) to the server.

#### `onAudio(callback: (data: Uint8Array) => void)`
Subscribes to incoming audio buffers from the AI.

#### `onVisemes(callback: (visemes: Viseme[]) => void)`
Subscribes to incoming viseme batches.

#### `disconnect()`
Closes the WebSocket connection.

---

## `TTSClient`

Dedicated client for converting text to high-quality streaming audio.

### Constructor

```typescript
new TTSClient(config: { apiKey: string })
```

### Methods

#### `synthesize(options: SynthesizeOptions): Promise<void>`
Starts synthesis and returns a promise that resolves when the stream finishes.

**Options:**
- `text` (string): The text to speak.
- `voice` (VoiceStyle): Optional.
- `language` (Language): Optional.
- `speed` (number): Optional. Default is 1.05.
- `steps` (number): Optional. Synthesis quality (1-50). Default is 24.
- `visemes` (boolean): Optional.
- `onAudio` (function): Callback for incoming audio buffers.
- `onVisemes` (function): Callback for animation/viseme data.
- `onError` (function): Callback for errors.

---

## Enums

### `VoiceStyle`
- `F1` to `F5`: Female voices.
- `M1` to `M5`: Male voices.

### `Language`
- `ENGLISH`: "en"
- `SPANISH`: "es"
- `FRENCH`: "fr"
- `PORTUGUESE`: "pt"
- `KOREAN`: "ko"

## Interfaces

### `Viseme`
- `v`: number – Viseme index.
- `c`: string – Character/phoneme label.
- `t`: number – Timestamp in seconds from utterance start.

The SDK accepts wire payload aliases (`id`, `char`, `timestamp`) and normalizes to `v`, `c`, `t`.

## Constants

### `AUDIO_CONFIG`
- `SAMPLE_RATE`: 44100
- `CHANNELS`: 1
- `CHUNK_DURATION_MS`: 20
- `CHUNK_SIZE`: 882
