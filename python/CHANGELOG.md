# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
