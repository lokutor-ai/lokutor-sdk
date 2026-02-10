# Lokutor Official SDKs

![Lokutor Hero](../assets/img/hero.png)

Build real-time, high-fidelity AI voice experiences with Lokutor. This repository contains the official client libraries for Lokutor's Voice Agent and streaming TTS APIs.

## 🚀 Available SDKs

| Language | Package | Documentation | Status |
| :--- | :--- | :--- | :--- |
| **Python** | [`lokutor`](https://pypi.org/project/lokutor/) | [Python README](python/README.md) | Stable |
| **JavaScript/TS** | `@lokutor/sdk` | [JavaScript README](js/README.md) | Stable |
| **Go** | `Coming Soon` | - | Planned |

---

## 🛠️ Quick Start

Lokutor SDKs provide a simple, unified interface for two-way voice conversations and streaming Text-to-Speech.

### Python

```bash
pip install lokutor
```

```python
from lokutor import VoiceAgentClient, TTSClient

# 1. Voice Conversation
agent = VoiceAgentClient(api_key="your_api_key")
agent.start_conversation()

# 2. Standalone TTS
tts = TTSClient(api_key="your_api_key")
tts.synthesize(text="Hello world", play=True)
```

### JavaScript / TypeScript

```bash
npm install @lokutor/sdk
```

```typescript
import { VoiceAgentClient, TTSClient } from '@lokutor/sdk';

// 1. Voice Conversation
const agent = new VoiceAgentClient({ apiKey: 'your_api_key', prompt: 'Hello' });
await agent.connect();

// 2. Standalone TTS
const tts = new TTSClient({ apiKey: 'your_api_key' });
await tts.synthesize({ text: 'Hello', onAudio: (buf) => play(buf) });
```

For full documentation, visit the respective [Python](python/) or [JavaScript](js/) folders.

---

## 📑 General Information

### API Access
To use these SDKs, you need an API key from the [Lokutor Dashboard](https://lokutor.com). 

### Security
These SDKs connect directly to `wss://api.lokutor.com`. Ensure your API key is kept secure and never exposed in client-side code for public applications.

## 🤝 Contributing

We welcome contributions! Please see the [Contributing Guide](python/CONTRIBUTING.md) inside the respective SDK folders.

## 📄 License

MIT License - See the [LICENSE](python/LICENSE) file for details.

## 📧 Support

- 🐛 [Report a Bug](https://github.com/lokutor-ai/lokutor-sdk/issues)
- 📧 [Contact Support](mailto:support@lokutor.com)
- 🌐 [Official Website](https://lokutor.com)
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
