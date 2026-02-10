# API Reference

Complete API documentation for the Lokutor Python SDK.

## Table of Contents

1. [VoiceAgentClient](#voiceagentclient) - Voice conversations
2. [TTSClient](#ttsclient) - Text-to-speech synthesis
3. [AudioRecorder](#audiorecorder) - Microphone input
4. [AudioPlayer](#audioplayer) - Audio playback
5. [Configuration](#configuration) - Enums and constants
6. [Exceptions](#exceptions) - Error types

---

## VoiceAgentClient

Main class for voice conversations with AI.

### Initialization

```python
from lokutor import VoiceAgentClient, VoiceStyle, Language

client = VoiceAgentClient(
    api_key: str,
    prompt: str = "You are a helpful assistant",
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
    server_url: str = "ws://localhost:8080/ws/agent",
    on_transcription: Optional[Callable[[str], None]] = None,
    on_agent_response: Optional[Callable[[str], None]] = None,
    on_audio_chunk: Optional[Callable[[bytes], None]] = None,
    on_error: Optional[Callable[[Exception], None]] = None
)
```

#### Parameters

- **api_key** (str): API key for authentication. Required.
- **prompt** (str): System prompt for the LLM. Instructs the AI behavior.
- **voice** (VoiceStyle): Voice style for speech synthesis. Default: F1.
- **language** (Language): Language for speech recognition and synthesis. Default: ENGLISH.
- **server_url** (str): WebSocket server URL. Default: ws://localhost:8080/ws/agent.
- **on_transcription** (Callable): Callback when user speech is transcribed.
  - Called with: `(text: str) -> None`
- **on_agent_response** (Callable): Callback when agent generates response.
  - Called with: `(text: str) -> None`
- **on_audio_chunk** (Callable): Callback as audio chunks arrive.
  - Called with: `(chunk: bytes) -> None`
- **on_error** (Callable): Callback when errors occur.
  - Called with: `(error: Exception) -> None`

#### Example

```python
def on_transcription(text):
    print(f"You: {text}")

def on_agent_response(text):
    print(f"Agent: {text}")

client = VoiceAgentClient(
    api_key="your-api-key",
    prompt="You are a helpful customer service agent",
    voice=VoiceStyle.F2,
    language=Language.ENGLISH,
    on_transcription=on_transcription,
    on_agent_response=on_agent_response
)
```

### Methods

#### `connect() -> bool`

Establish WebSocket connection to server.

**Returns:** `True` if connection successful, `False` otherwise.

**Raises:** `ConnectionError` if connection fails.

```python
if client.connect():
    print("Connected!")
else:
    print("Connection failed")
```

#### `disconnect() -> None`

Close WebSocket connection gracefully.

```python
client.disconnect()
```

#### `start_conversation() -> None`

Start listening and having a conversation.

Blocking call that runs until user exits (Ctrl+C) or conversation ends.

**Raises:** 
- `ConnectionError` if not connected
- `AudioError` if audio recording fails
- `ServerError` if server returns error

```python
try:
    client.connect()
    client.start_conversation()
except KeyboardInterrupt:
    print("Conversation ended")
finally:
    client.disconnect()
```

#### `set_prompt(prompt: str) -> None`

Update system prompt during conversation.

**Parameters:**
- **prompt** (str): New system prompt

**Raises:** `InvalidArgumentError` if prompt is empty

```python
client.set_prompt("You are now a Spanish tutor")
```

#### `set_voice(voice: VoiceStyle) -> None`

Change voice style during conversation.

**Parameters:**
- **voice** (VoiceStyle): New voice style

```python
client.set_voice(VoiceStyle.M1)
```

#### `set_language(language: Language) -> None`

Change language during conversation.

**Parameters:**
- **language** (Language): New language

```python
client.set_language(Language.SPANISH)
```

#### `reset() -> None`

Reset conversation state (clears history).

**Raises:** `ServerError` if reset fails on server

```python
client.reset()  # Start fresh conversation
```

### Properties

#### `connected -> bool`

Read-only. Whether client is connected to server.

```python
if client.connected:
    print("Client is connected")
```

#### `prompt -> str`

Read-only. Current system prompt.

```python
print(f"Current prompt: {client.prompt}")
```

#### `voice -> VoiceStyle`

Read-only. Current voice style.

```python
print(f"Using voice: {client.voice}")
```

#### `language -> Language`

Read-only. Current language.

```python
print(f"Using language: {client.language}")
```

---

## TTSClient

Standalone text-to-speech synthesis client.

### Initialization

```python
from lokutor import TTSClient, VoiceStyle, Language

client = TTSClient(
    api_key: str,
    server_url: str = "ws://localhost:8080/ws/handler",
    on_chunk: Optional[Callable[[bytes], None]] = None,
    on_error: Optional[Callable[[Exception], None]] = None
)
```

#### Parameters

- **api_key** (str): API key for authentication. Required.
- **server_url** (str): WebSocket server URL. Default: ws://localhost:8080/ws/handler.
- **on_chunk** (Callable): Callback for audio chunks.
  - Called with: `(chunk: bytes) -> None`
- **on_error** (Callable): Callback for errors.
  - Called with: `(error: Exception) -> None`

#### Example

```python
def on_chunk(chunk):
    print(f"Received {len(chunk)} bytes")

client = TTSClient(
    api_key="your-api-key",
    on_chunk=on_chunk
)
```

### Methods

#### `connect() -> bool`

Connect to TTS server.

**Returns:** `True` if successful, `False` otherwise.

```python
if client.connect():
    print("TTS server connected")
```

#### `synthesize(text: str, voice: VoiceStyle, language: Language, play: bool = False) -> bytes`

Synthesize text to speech.

**Parameters:**
- **text** (str): Text to synthesize. Required.
- **voice** (VoiceStyle): Voice style. Required.
- **language** (Language): Language. Required.
- **play** (bool): Play audio immediately. Default: False.

**Returns:** Audio data as bytes (PCM16 @ 16kHz).

**Raises:**
- `InvalidArgumentError` if parameters invalid
- `ServerError` if synthesis fails
- `AudioError` if playback fails (when play=True)

```python
audio = client.synthesize(
    text="Hello, how can I help?",
    voice=VoiceStyle.F1,
    language=Language.ENGLISH,
    play=True
)
```

#### `save_audio(audio_data: bytes, filepath: str) -> None`

Save audio to WAV file.

**Parameters:**
- **audio_data** (bytes): Audio data from synthesize()
- **filepath** (str): Path to save WAV file

**Raises:** `AudioError` if save fails

```python
audio = client.synthesize(
    text="Goodbye!",
    voice=VoiceStyle.M2,
    language=Language.ENGLISH
)
client.save_audio(audio, "goodbye.wav")
```

#### `disconnect() -> None`

Close connection to server.

```python
client.disconnect()
```

### Properties

#### `connected -> bool`

Read-only. Connection status.

```python
if client.connected:
    print("Client is connected")
```

---

## Module Functions

### `simple_tts()`

Convenient function for one-off TTS synthesis.

```python
from lokutor import simple_tts

audio = simple_tts(
    text: str,
    api_key: str,
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
    output_file: Optional[str] = None,
    play: bool = False
) -> bytes
```

**Parameters:**
- **text** (str): Text to synthesize. Required.
- **api_key** (str): API key. Required.
- **voice** (VoiceStyle): Voice style.
- **language** (Language): Language.
- **output_file** (str): File path to save as WAV. Optional.
- **play** (bool): Play audio after synthesis.

**Returns:** Audio bytes.

**Example:**

```python
# Simple synthesis
audio = simple_tts(
    text="Welcome!",
    api_key="your-key"
)

# Save to file
audio = simple_tts(
    text="Save this",
    api_key="your-key",
    output_file="output.wav"
)

# Play immediately
simple_tts(
    text="Play this",
    api_key="your-key",
    play=True
)
```

### `simple_conversation()`

Helper for simple voice conversations.

```python
from lokutor import simple_conversation

simple_conversation(
    api_key: str,
    prompt: str = "You are a helpful assistant",
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH
) -> None
```

**Parameters:**
- **api_key** (str): API key. Required.
- **prompt** (str): System prompt.
- **voice** (VoiceStyle): Voice style.
- **language** (Language): Language.

**Example:**

```python
from lokutor import simple_conversation

simple_conversation(
    api_key="your-key",
    prompt="You are a Spanish tutor"
)
```

---

## AudioRecorder

Microphone input with voice activity detection.

### Initialization

```python
from lokutor.audio import AudioRecorder

recorder = AudioRecorder(
    sample_rate: int = 16000,
    channels: int = 1,
    chunk_size: int = 1024
)
```

#### Parameters

- **sample_rate** (int): Sample rate in Hz. Default: 16000.
- **channels** (int): Audio channels (1=mono, 2=stereo). Default: 1.
- **chunk_size** (int): Chunk size in frames. Default: 1024.

### Methods

#### `list_devices() -> None`

List available audio input devices.

```python
recorder = AudioRecorder()
recorder.list_devices()
```

#### `start_recording() -> None`

Begin recording from microphone.

```python
recorder.start_recording()
```

#### `stop_recording() -> None`

Stop recording.

```python
recorder.stop_recording()
```

#### `get_chunk() -> bytes`

Get next audio chunk.

**Returns:** Audio chunk as bytes.

```python
while recorder.is_recording():
    chunk = recorder.get_chunk()
    process(chunk)
```

#### `is_recording() -> bool`

Check if currently recording.

**Returns:** True if recording, False otherwise.

```python
if recorder.is_recording():
    print("Recording...")
```

### Properties

#### `SILENCE_THRESHOLD`

Silence detection threshold. Lower = more sensitive.

```python
print(f"Silence threshold: {recorder.SILENCE_THRESHOLD}")
```

---

## AudioPlayer

Audio output with multiple playback methods.

### Initialization

```python
from lokutor.audio import AudioPlayer

player = AudioPlayer(
    sample_rate: int = 16000,
    channels: int = 1
)
```

#### Parameters

- **sample_rate** (int): Sample rate in Hz. Default: 16000.
- **channels** (int): Audio channels. Default: 1.

### Methods

#### `list_devices() -> None`

List available audio output devices.

```python
player = AudioPlayer()
player.list_devices()
```

#### `play_bytes(audio_data: bytes) -> None`

Play raw PCM16 audio data.

**Parameters:**
- **audio_data** (bytes): PCM16 audio at 16kHz.

**Raises:** `AudioError` if playback fails.

```python
import numpy as np

# Generate 1 second of 440Hz sine wave
sample_rate = 16000
t = np.linspace(0, 1, sample_rate)
audio = np.sin(2 * np.pi * 440 * t)
audio = (audio * 32767).astype(np.int16)
audio_bytes = audio.tobytes()

player = AudioPlayer()
player.play_bytes(audio_bytes)
```

#### `play_file(filepath: str) -> None`

Play audio from WAV file.

**Parameters:**
- **filepath** (str): Path to WAV file.

**Raises:** `AudioError` if file invalid or playback fails.

```python
player = AudioPlayer()
player.play_file("output.wav")
```

#### `play_chunk(chunk: bytes) -> None`

Play audio chunk (for streaming).

**Parameters:**
- **chunk** (bytes): Audio chunk.

```python
for chunk in audio_stream:
    player.play_chunk(chunk)
```

---

## Configuration

### VoiceStyle Enum

Available voice styles.

```python
from lokutor import VoiceStyle

# Female voices
VoiceStyle.F1  # Warm, friendly
VoiceStyle.F2  # Bright, energetic
VoiceStyle.F3  # Calm, soothing
VoiceStyle.F4  # Professional
VoiceStyle.F5  # Natural, conversational

# Male voices
VoiceStyle.M1  # Deep, authoritative
VoiceStyle.M2  # Clear, neutral
VoiceStyle.M3  # Young, casual
VoiceStyle.M4  # Warm, approachable
VoiceStyle.M5  # Dynamic, expressive
```

### Language Enum

Supported languages.

```python
from lokutor import Language

Language.ENGLISH      # en-US
Language.SPANISH      # es-ES
Language.FRENCH       # fr-FR
Language.GERMAN       # de-DE
Language.ITALIAN      # it-IT
Language.PORTUGUESE   # pt-BR
```

### Audio Constants

```python
from lokutor.config import (
    SAMPLE_RATE,        # 16000 Hz
    CHANNELS,           # 1 (mono)
    CHUNK_SIZE,         # 1024 frames
    SILENCE_THRESHOLD   # 100 (RMS value)
)
```

---

## Exceptions

Custom exception hierarchy for error handling.

### Exception Hierarchy

```
LokurorError (base)
├── ConnectionError
├── AuthenticationError
├── ServerError
├── TimeoutError
├── InvalidArgumentError
└── AudioError
```

### LokurorError

Base exception for all Lokutor errors.

```python
from lokutor import LokurorError

try:
    # Code
    pass
except LokurorError as e:
    print(f"Lokutor error: {e}")
```

### ConnectionError

WebSocket or network connection issues.

```python
from lokutor import ConnectionError as LokurorConnectionError

try:
    client.connect()
except LokurorConnectionError:
    print("Failed to connect to server")
```

### AuthenticationError

Invalid API key or authentication failure.

```python
from lokutor import AuthenticationError

try:
    client = VoiceAgentClient(api_key="invalid")
    client.connect()
except AuthenticationError:
    print("Invalid API key")
```

### ServerError

Remote server returned error.

```python
from lokutor import ServerError

try:
    client.start_conversation()
except ServerError as e:
    print(f"Server error: {e}")
```

### TimeoutError

Operation exceeded timeout.

```python
from lokutor import TimeoutError as LokurorTimeoutError

try:
    result = client.some_operation()
except LokurorTimeoutError:
    print("Operation timed out")
```

### InvalidArgumentError

Invalid parameters provided.

```python
from lokutor import InvalidArgumentError

try:
    client.set_prompt("")  # Empty prompt
except InvalidArgumentError:
    print("Invalid argument provided")
```

### AudioError

Audio recording or playback issues.

```python
from lokutor import AudioError

try:
    recorder.start_recording()
except AudioError:
    print("Audio device error")
```

---

## Complete Example

```python
from lokutor import (
    VoiceAgentClient, TTSClient,
    VoiceStyle, Language,
    LokurorError, AuthenticationError
)

def main():
    api_key = "your-api-key"
    
    # Example 1: Voice conversation
    try:
        client = VoiceAgentClient(
            api_key=api_key,
            prompt="You are a helpful AI assistant",
            voice=VoiceStyle.F1,
            language=Language.ENGLISH
        )
        
        if client.connect():
            client.start_conversation()
        else:
            print("Failed to connect")
            
    except KeyboardInterrupt:
        print("\nConversation ended")
    except AuthenticationError:
        print("Invalid API key")
    except LokurorError as e:
        print(f"Error: {e}")
    finally:
        client.disconnect()
    
    # Example 2: Text-to-speech
    try:
        tts = TTSClient(api_key=api_key)
        
        if tts.connect():
            audio = tts.synthesize(
                text="Thank you for using Lokutor",
                voice=VoiceStyle.M1,
                language=Language.ENGLISH,
                play=True
            )
            
            tts.save_audio(audio, "thankyou.wav")
        
    except LokurorError as e:
        print(f"TTS Error: {e}")
    finally:
        tts.disconnect()

if __name__ == "__main__":
    main()
```

---

## Version History

SDK Version: **1.0.0**

See [CHANGELOG.md](CHANGELOG.md) for detailed version history.

## Links

- 📖 [Main README](README.md)
- 🔧 [Development Guide](DEVELOPMENT.md)
- 🐛 [Troubleshooting](TROUBLESHOOTING.md)
- 📦 [GitHub Repository](https://github.com/lokutor-ai/lokutor_tts)
