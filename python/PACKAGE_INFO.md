# Package Information

## Lokutor Python SDK

Production-grade Python SDK for voice conversations with AI.

### Package Details

- **Name**: lokutor
- **Version**: 1.0.0
- **Python**: 3.8+
- **License**: MIT
- **Author**: Lokutor Team
- **Repository**: https://github.com/lokutor-ai/lokutor_tts

### What's Included

```
lokutor/
├── client.py              # VoiceAgentClient & TTSClient
├── config.py              # Configuration, VoiceStyle, Language enums
└── __init__.py            # Main module exports
```

### Dependencies

**Runtime:**
- `websocket-client>=1.0.0,<2.0.0` - WebSocket protocol
- `pyaudio>=0.2.13` - Audio I/O
- `python-dotenv>=1.0.0` - Environment configuration

**Optional (Development):**
- `pytest>=7.0` - Testing framework
- `pytest-cov>=4.0` - Code coverage
- `black>=23.0` - Code formatting
- `flake8>=6.0` - Linting

### Installation Methods

**PyPI (Recommended)**
```bash
pip install lokutor
```

**From Source**
```bash
git clone https://github.com/lokutor-ai/lokutor-sdk
cd sdk/python
pip install -e .
```

**With Development Tools**
```bash
pip install -e ".[dev]"
```

### System Requirements

- **Python**: 3.8 or higher
- **OS**: Linux, macOS, Windows
- **Audio**: Microphone and speakers (recommended)
- **Network**: Internet for API calls to Lokutor server

### Key Features

✅ **Real-time Voice Conversations**
- Automatic voice activity detection
- Multi-turn conversation support
- Interruption handling
- System prompt customization

✅ **Text-to-Speech Synthesis**
- 10 professional voices (5 female, 5 male)
- 6 supported languages
- Real-time audio streaming
- WAV file export

✅ **Production Ready**
- Error handling with custom exceptions
- WebSocket connection management
- Automatic reconnection
- Comprehensive logging support

✅ **Easy to Use**
- Simple API requiring minimal code
- Callbacks for advanced use cases
- Well-documented with examples
- Type hints throughout

### Quick Start

```python
from lokutor import VoiceAgentClient, VoiceStyle, Language

# Voice conversation
client = VoiceAgentClient(
    api_key="your-api-key",
    prompt="You are a helpful assistant",
    voice=VoiceStyle.F1,
    language=Language.ENGLISH
)

client.start_conversation()
```

### Use Cases

1. **Voice Chatbots** - Interactive conversational AI
2. **Voice Assistants** - Personal AI assistant with voice
3. **Customer Service** - Voice-based support systems
4. **Audio Content** - Generate speech from text
5. **Accessibility** - Text-to-speech for accessibility
6. **Voice Transcription** - Speech-to-text with LLM processing
7. **Multilingual Apps** - Support multiple languages
8. **Real-time Communication** - Low-latency voice interaction

### Architecture

```
User Input (Microphone)
    ↓
AudioRecorder (VAD + PCM16 capture)
    ↓
WebSocket to Lokutor Server
    ↓
Server Process:
  1. Whisper (Speech-to-Text)
  2. Groq LLM (Generate Response)
  3. TTS Engine (Text-to-Speech)
    ↓
WebSocket Response (Audio Stream)
    ↓
AudioPlayer (Playback)
    ↓
Output (Speakers)
```

### Performance Characteristics

- **Latency**: 200-500ms (depends on network and server)
- **Audio Quality**: 16kHz PCM16 mono
- **Memory**: ~50-100MB per active session
- **CPU**: Minimal (most processing on server)
- **Concurrency**: Supports multiple simultaneous sessions

### Compatibility

- **Python Versions**: 3.8, 3.9, 3.10, 3.11, 3.12
- **Operating Systems**:
  - macOS 10.14+ (Intel & Apple Silicon)
  - Ubuntu 18.04+ (Linux)
  - Windows 10+ (with audio drivers)
- **Server**: Lokutor Go server v1.2.0+

### File Sizes

- SDK Package: ~50 KB
- With Dependencies: ~10-20 MB
- After Installation: ~50 MB

### License

MIT License - Free for commercial and personal use

See [LICENSE](LICENSE) for full details.

### Support

- 📖 [Documentation](README.md)
- 🔧 [API Reference](API_REFERENCE.md)
- 🐛 [Troubleshooting](TROUBLESHOOTING.md)
- 💻 [Development](DEVELOPMENT.md)
- 🤝 [Contributing](CONTRIBUTING.md)
- 🐛 [Issues](https://github.com/lokutor-ai/lokutor-sdk/issues)
- 💬 [Discussions](https://github.com/lokutor-ai/lokutor-sdk/discussions)

### Version History

- **1.0.0** (Jan 15, 2026) - Initial release
  - VoiceAgentClient for conversations
  - TTSClient for synthesis
  - Full audio I/O support
  - Exception hierarchy
  - Comprehensive documentation

### Roadmap

**Planned Features:**
- Async/await support
- Configuration file support
- Advanced audio processing
- Custom voice models
- Voice cloning
- Real-time translation
- Voice emotion detection
- Advanced conversation memory

### Security

- ✅ API key authentication
- ✅ Secure WebSocket (WSS) support
- ✅ Input validation
- ✅ No data storage locally
- ✅ MIT licensed (open source)

**Recommendations:**
- Keep API keys in environment variables
- Use secure WSS connections in production
- Validate user inputs
- Monitor API usage

### Contributing

Contributions welcome! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Changelog

See [CHANGELOG.md](CHANGELOG.md) for detailed release notes and changes.

### Citation

If you use Lokutor in research or production, please cite:

```bibtex
@software{lokutor2026,
  title={Lokutor: Voice AI SDK},
  author={Lokutor Team},
  url={https://github.com/lokutor-ai/lokutor-sdk},
  year={2026}
}
```

### Contact

- 📧 Email: support@lokutor.com
- 🐦 Twitter: [@lokutor](https://twitter.com/lokutor)
- 💼 LinkedIn: [Lokutor](https://linkedin.com/company/lokutor)

---

**Built with ❤️ for natural voice conversations**

Last Updated: January 2026
