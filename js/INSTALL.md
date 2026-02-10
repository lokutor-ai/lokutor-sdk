# Installation Guide

## Basic Installation

```bash
npm install @lokutor/sdk
```

## Platform-Specific Notes

### Node.js

To use the voice agent in a terminal or backend service, you'll need additional libraries for hardware access:

```bash
npm install node-record-lpcm16 speaker
```

**Linux Users:** You may need to install ALSA or PulseAudio development headers:
```bash
sudo apt-get install libasound2-dev
```

### Browser

No extra steps are needed. The SDK uses standard `WebSocket` and `Uint8Array` types that are natively supported by modern browsers.
