# Lokutor Voice Agent Python SDK

[![Python Version](https://img.shields.io/badge/python-3.8+-blue.svg)](https://www.python.org/downloads/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![PyPI Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](https://pypi.org/project/lokutor/)

A powerful, production-grade Python SDK for real-time voice conversations with AI. 
Seamlessly transcribe speech, generate intelligent responses, and synthesize natural speech—all in real-time.

## Features

- 🎤 **Real-time Voice Input** - Automatic microphone recording with voice activity detection
- 🔊 **Natural Speech Output** - High-quality text-to-speech synthesis (44.1kHz)
- 🧠 **Intelligent Conversations** - LLM-powered responses with context awareness
- 🎯 **10 Voice Options** - Choose from diverse voices (F1-F5 female, M1-M5 male)
- 🌍 **Multi-Language** - English, Spanish, French, and more
- ⚡ **Low Latency** - Raw binary WebSocket streaming for real-time interaction
- 🛡️ **Production-Ready** - Thread-safe WebSocket management and proper audio buffering
- 📚 **Easy API** - Minimal code required to get started

## Installation

```bash
pip install lokutor
```

Or from source:

```bash
git clone https://github.com/team-hashing/lokutor_tts
cd sdk/python
pip install -e .
```

Requires `PyAudio` and `websocket-client`.

## Quick Start

### Voice Conversation (Start Speaking!)

```python
from lokutor import VoiceAgentClient
from lokutor.config import VoiceStyle, Language

client = VoiceAgentClient(api_key="your-api-key")
client.start_conversation()
```

### Standalone Text-to-Speech

```python
from lokutor import TTSClient, VoiceStyle

client = TTSClient(api_key="your-api-key")
client.synthesize(
    text="Hello world, this is a test of Lokutor streaming TTS.",
    voice=VoiceStyle.F1,
    play=True
)
```

## Core APIs

### 1. `VoiceAgentClient`

Full conversational interface that handles VAD, STT, LLM, and TTS orchestration.

```python
client = VoiceAgentClient(
    api_key="your-key",
    prompt="You are a helpful customer service agent",
    voice=VoiceStyle.F2,
    language=Language.ENGLISH
)
client.start_conversation()
```

**Callbacks:**
- `on_transcription(text)`: Called when your speech is transcribed.
- `on_response(text)`: Called when the agent's text response is ready.
- `on_error(error)`: Called when a connection or server error occurs.

### 2. `TTSClient`

For converting text to high-quality streaming audio.

```python
client = TTSClient(api_key="your-key")
client.synthesize(
    text="Welcome to our platform",
    voice=VoiceStyle.M2,
    play=True,
    block=True
)
```

## Configuration

### Voice Styles

| Female Voices | Male Voices |
|--------------|-----------|
| `VoiceStyle.F1` | `VoiceStyle.M1` |
| `VoiceStyle.F2` | `VoiceStyle.M2` |
| `VoiceStyle.F3` | `VoiceStyle.M3` |
| `VoiceStyle.F4` | `VoiceStyle.M4` |
| `VoiceStyle.F5` | `VoiceStyle.M5` |

### Supported Languages

- `Language.ENGLISH`
- `Language.SPANISH`
- `Language.FRENCH`
- `Language.PORTUGUESE`
- `Language.KOREAN`

## Usage Examples

### Full Conversation with Callbacks

```python
from lokutor import VoiceAgentClient, VoiceStyle

def handle_user_text(text):
    print(f"User said: {text}")

def handle_agent_text(text):
    print(f"Agent replied: {text}")

client = VoiceAgentClient(
    api_key="demo",
    prompt="You are a friendly pirate assistant",
    on_transcription=handle_user_text,
    on_response=handle_agent_text
)

client.start_conversation()
```

### Scripted TTS with Visemes

```python
from lokutor import TTSClient

def handle_visemes(visemes):
    # Perfect for syncing 3D character lip-sync
    print(f"Syncing lips: {visemes}")

client = TTSClient(api_key="demo")
client.synthesize(
    text="Look at my lips moving!",
    visemes=True,
    on_visemes=handle_visemes
)
```

## Troubleshooting

### Connection Issues
The SDK defaults to `wss://api.lokutor.com`. If you are running a local server, override the `server_url`:

```python
client = VoiceAgentClient(
    api_key="demo",
    server_url="ws://localhost:8080/ws/agent"
)
```

### PyAudio Installation
If you encounter issues installing `pyaudio`, you may need to install portaudio first:
- **macOS**: `brew install portaudio`
- **Linux**: `sudo apt-get install python3-pyaudio` or `sudo apt-get install portaudio19-dev`

## License
MIT License
