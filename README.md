# Lokutor SDKs

Official SDKs for building voice-enabled AI applications with Lokutor.

## Python SDK

The official Python SDK for real-time voice conversations with AI.

**Features:**
- 🎤 Real-time voice input with automatic speech recognition
- 🔊 High-quality text-to-speech synthesis
- 🧠 LLM-powered conversational AI
- 🎯 10 professional voices (F1-F5 female, M1-M5 male)
- 🌍 6 supported languages
- ⚡ Low-latency WebSocket streaming
- 🛡️ Production-ready error handling

**Quick Start:**

```python
from lokutor import VoiceAgentClient, VoiceStyle, Language

client = VoiceAgentClient(
    api_key="your-api-key",
    prompt="You are a helpful assistant",
    voice=VoiceStyle.F1,
    language=Language.ENGLISH
)

client.start_conversation()
```

**Installation:**

```bash
pip install lokutor
```

Or from source:

```bash
cd python
pip install -e .
```

**Examples:**
- [Simple Chat](python/examples/simple_chat.py) - Basic voice conversation
- [Advanced Chat](python/examples/advanced_chat.py) - Custom prompts and callbacks

**Documentation:**
- [README](python/README.md) - Feature overview and quick start
- [Installation Guide](python/INSTALL.md) - Detailed setup instructions
- [API Reference](python/API_REFERENCE.md) - Complete API documentation
- [Troubleshooting](python/TROUBLESHOOTING.md) - Common issues and solutions
- [Development Guide](python/DEVELOPMENT.md) - Contributing and development

## License

MIT License - See [LICENSE](python/LICENSE) for details

## Support

- 📖 [Documentation](python/README.md)
- 🐛 [GitHub Issues](https://github.com/lokutor-ai/lokutor_tts/issues)
- 💬 [GitHub Discussions](https://github.com/lokutor-ai/lokutor_tts/discussions)
- 📧 support@lokutor.com

**Planned features:**
- Full async/await support
- Strong typing and null safety
- Integration with ASP.NET
- Unit testing helpers

---

### JavaScript/TypeScript SDK

Coming soon! For web and Node.js applications.

**Planned features:**
- Browser and Node.js support
- TypeScript support
- React hooks
- Error handling and retry logic

---

## Server Setup

Before using any SDK, you need to run the server:

```bash
# Start the server
GROQ_API_KEY="your-groq-key" go run cmd/server/main.go
```

The server will:
1. Transcribe your speech (Whisper)
2. Generate intelligent responses (Groq LLM)
3. Convert text to speech (Versa TTS)
4. Stream everything in real-time

## Architecture

Each SDK implements the same WebSocket protocol:

```
Client                          Server
  |                               |
  +------ audio chunks ---------->|
  |                               |
  |        [Whisper]             |
  |        [Groq LLM]            |
  |        [Versa TTS]           |
  |                               |
  |<---- audio response ----------+
  |                               |
```

## Choosing Your SDK

- **Python**: Fastest to prototype, great for ML/data work
- **C#**: Enterprise applications, Windows-first development
- **JavaScript**: Web apps, real-time web, Electron apps

## Contributing

Want to contribute an SDK or improve existing ones?

1. Follow the WebSocket protocol
2. Implement the VoiceStyle and Language enums
3. Handle interruptions and silence detection
4. Add examples and comprehensive docs

See [PROTOCOL.md](PROTOCOL.md) for technical details.

## Support

- 📖 [API Documentation](../docs/public_api_docs.md)
- 💬 [Issues & Questions](https://github.com/lokutor/lokutor-tts/issues)
- 🐛 [Bug Reports](https://github.com/lokutor/lokutor-tts/issues/new?template=bug_report.md)

## License

MIT - See LICENSE in repository root
