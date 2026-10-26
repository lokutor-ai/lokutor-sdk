# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- `VoiceAgentClient(language="auto")` (or `""`) sends `{"type":"language","data":""}`: the agent follows the
  caller's language. Omitting `language` is unchanged (`Language.ENGLISH`).

## [1.6.0] - 2026-10-21

- Custom vocabulary: `keyterms` on `STTClient.transcribe()` and `SpeechToTextClient(...)`, names, brands and terms
  to write your way when what is heard is close (up to 100; send `language` too).
  https://docs.lokutor.com/guides/custom-vocabulary

## [1.5.0] - 2026-10-18

- `on_tool_call(name, args, call_id)`: the call id is passed, so `submit_tool_result(call_id, ...)` can be
  used. Two-argument handlers keep working.
- `rates` is sent once, last in the configuration. It was sent in reply to every `server_rates`, which the
  server answers with another `server_rates`, so the two answered each other for the whole session.
- Imports on Python 3.8 and 3.9 (a `str | dict` annotation raised `TypeError` at import).
- The audio device opens only when audio is played or recorded: `TTSClient`, `STTClient` and
  `VoiceAgentClient` construct without pyaudio, and `disconnect()` needs none.
- A plain string is accepted for `voice` and `language` (for example a `clone_...` voice id); `.value` was
  read unconditionally and crashed the configuration.
- Version numbers agree (setup.py said 1.2.3, the README 1.0.0).

## [1.0.0] - 2026-01-15

### Added
- **VoiceAgentClient**: Full-featured voice conversation client with:
  - Real-time voice input/output with voice activity detection
  - Multi-turn conversation support
  - Automatic message routing to LLM with text-to-speech synthesis
  - Session management with system prompts, voice styles, and language settings
  - WebSocket-based streaming for low latency
  
- **TTSClient**: Standalone text-to-speech synthesis client with:
  - Multiple voice styles (F1-F5 female, M1-M5 male voices)
  - Multi-language support (English, Spanish, French, German, Italian, Portuguese)
  - Real-time audio streaming with chunk callbacks
  - WAV file export functionality
  - Simple one-line synthesis with `simple_tts()` helper function

- **AudioRecorder**: Microphone input with:
  - Real-time voice activity detection (VAD)
  - Automatic silence suppression
  - Configurable sensitivity levels
  - PCM16 @ 16kHz audio capture

- **AudioPlayer**: Audio playback with:
  - Real-time stream playback for synthesis results
  - WAV file playback support
  - Direct PCM16 data playback
  - Proper audio buffer management

- **Configuration System**:
  - VoiceStyle enum with 10 predefined voices (F1-F5, M1-M5)
  - Language enum with 6 supported languages
  - Customizable audio parameters (sample rate, channels, buffer size)

- **Exception Hierarchy**:
  - LokurorError (base exception)
  - ConnectionError (WebSocket/network issues)
  - AuthenticationError (API key failures)
  - ServerError (remote server errors)
  - TimeoutError (operation timeouts)
  - InvalidArgumentError (bad parameters)
  - AudioError (audio processing failures)

- **Examples**:
  - `quickstart.py`: Simple voice conversation example
  - `simple_chat.py`: Basic text conversation example
  - `advanced_chat.py`: Advanced conversation with custom voices and languages

- **Documentation**:
  - Comprehensive README with quickstart and features
  - Getting started guide
  - Protocol documentation

### Changed
- Upgraded package version from 0.1.0 to 1.0.0
- Enhanced setup.py with production-grade metadata
- Improved AudioPlayer with file I/O capabilities
- Better error handling with custom exception types

### Fixed
- WebSocket connection stability
- Audio buffer management
- Voice activity detection sensitivity

## [0.1.0] - 2025-12-01

### Added
- Initial SDK release
- Basic VoiceAgentClient functionality
- AudioRecorder and AudioPlayer classes
- Voice style and language configuration
- WebSocket communication layer
