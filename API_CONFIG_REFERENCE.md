# Lokutor Python SDK API Reference

The Lokutor SDK provides classes and tools for real-time voice conversations and high-quality TTS.

## Table of Contents
1. [Installation & Imports](#1-installation--imports)
2. [VoiceAgentClient (Conversational AI)](#2-voiceagentclient-conversational-ai)
3. [TTSClient (Text-to-Speech)](#3-ttsclient-text-to-speech)
4. [Utility Functions](#4-utilty-functions)
5. [Enums & Constants](#5-enums--constants)
6. [Advanced: Interruption Management](#6-advanced-interruption-management)

---

## 1. Installation & Imports

To use the SDK, import the main components from the `lokutor` package:

```python
from lokutor import (
    VoiceAgentClient, 
    TTSClient, 
    VoiceStyle, 
    Language,
    Viseme,
    simple_conversation,
    simple_tts
)
```

---

## 2. VoiceAgentClient (Conversational AI)
The `VoiceAgentClient` manages a full-duplex WebSocket connection for live conversations.

### Constructor Configuration
```python
client = VoiceAgentClient(
    api_key="your-key",             # String: Required
    prompt="You are...",            # String: Optional (system role)
    voice=VoiceStyle.F1,            # VoiceStyle: Optional (defaults to F1)
    language=Language.ENGLISH,      # Language: Optional (defaults to English)
    visemes=False,                  # Boolean: Optional (request lip-sync data)
    tools=None,                     # List: Optional (JSON Tool definitions)
    
    # Optional direct callbacks (Legacy property support)
    on_transcription=handler,       # (text: str) -> None
    on_response=handler,            # (text: str) -> None
    on_audio=handler,               # (data: bytes) -> None
    on_visemes=handler,             # (visemes: list[Viseme]) -> None
    on_tool_call=handler,           # (name: str, args: str) -> None
    on_status=handler,              # (status: str) -> None
    on_error=handler                # (error_msg: str) -> None
)
```

### Methods
| Method | Description |
| :--- | :--- |
| `start_conversation()` | Starts the microphone input, playback output, and blocks until interrupted. |
| `connect()` | Opens the WebSocket connection without starting audio hardware. |
| `disconnect()` | Safely closes the session and releases audio hardware. |
| `update_prompt(new_prompt)` | Changes the agent's persona mid-conversation. |
| `get_transcript()` | Returns a list of message objects (`role`, `text`, `timestamp`). |
| `get_transcript_text()` | Returns a formatted string of the whole conversation. |
| `on(event, callback)` | Register an event listener. Supports: `"transcription"`, `"response"`, `"audio"`, `"visemes"`, `"status"`, `"tool_call"`, `"error"`. |

---

## 3. TTSClient (Text-to-Speech)
The `TTSClient` is used for non-conversational, high-quality audio generation.

### Constructor
```python
tts = TTSClient(api_key="your-key")
```

### `synthesize()` Parameters
| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `text` | `str` | *(Required)* | Text to speak. |
| `voice` | `VoiceStyle` | `F1` | Voice style enum. |
| `language` | `Language` | `ENGLISH` | Language code enum. |
| `speed` | `float` | `1.05` | Speed multiplier (e.g. 1.2 for faster). |
| `steps` | `int` | `24` | Quality (number of diffusion steps). |
| `visemes` | `bool` | `False` | Request lip-sync markers. |
| `play` | `bool` | `True` | Automatically play through speakers. |
| `block` | `bool` | `True` | Wait until synthesis AND playback finish. |

---

## 4. Utility Functions

### `simple_conversation(...)`
A wrapper that initializes a `VoiceAgentClient` and immediately starts the conversation loop.
*   Arguments: `api_key`, `prompt`, `voice`, `language`.

### `simple_tts(...)`
A wrapper for quick TTS synthesis.
*   Arguments: `api_key`, `text`, `voice`, `language`.

---

## 5. Enums & Constants

### VoiceStyle (str Enum)
*   **Female**: `F1`, `F2`, `F3`, `F4`, `F5`
*   **Male**: `M1`, `M2`, `M3`, `M4`, `M5`

### Language (str Enum)
*   `ENGLISH` ("en")
*   `SPANISH` ("es")
*   `FRENCH` ("fr")
*   `PORTUGUESE` ("pt")
*   `KOREAN` ("ko")

---

## 6. Advanced: Interruption Management
The agent can be interrupted by the user speaking. This is handled by Voice Activity Detection (VAD) on the server.

### Why does it interrupt?
The server sends an interrupted status message if it detects significant noise from your microphone while the agent is still speaking. 

### How to tune it:
1.  **Hardware**: Use a headset to prevent speaker loopback into your microphone.
2.  **Gain**: Manually lower your microphone input level in your Operating System settings.
3.  **Prompt Instruction**: Add instructions like "Do not stop speaking if you hear background noise" to your prompt. Note that server-side VAD takes precedence.

### Configurable Interruption Thresholds?
No. There is currently no parameter in the SDK to adjust the interruption sensitivity. It is a fixed server-side threshold.
