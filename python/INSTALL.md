# Installation Guide

Complete installation instructions for the Lokutor Python SDK.

## Table of Contents

1. [Requirements](#requirements)
2. [Standard Installation](#standard-installation)
3. [Development Installation](#development-installation)
4. [Platform-Specific Setup](#platform-specific-setup)
5. [Verification](#verification)
6. [Troubleshooting](#troubleshooting)
7. [Uninstallation](#uninstallation)

## Requirements

### System Requirements

- **Python**: 3.8 or higher
- **OS**: macOS, Linux, or Windows
- **Audio**: Microphone and speakers (optional but recommended)
- **Network**: Internet connection for server communication
- **Disk Space**: ~100MB for installation and dependencies

### Check Python Version

```bash
python --version
# Should output: Python 3.8.0 or higher

python3 --version  # macOS/Linux might use python3
```

If Python is not installed or version is below 3.8:
- **macOS**: `brew install python@3.12`
- **Linux**: `sudo apt install python3.12 python3.12-venv`
- **Windows**: Download from [python.org](https://www.python.org/downloads/)

## Standard Installation

### From PyPI (Recommended)

```bash
# Install latest version
pip install lokutor

# Install specific version
pip install lokutor==1.0.0

# Upgrade existing installation
pip install --upgrade lokutor
```

### Verify Installation

```bash
python -c "import lokutor; print(lokutor.__version__)"
# Output: 1.0.0
```

## Development Installation

For contributing or using the latest development version.

### Clone Repository

```bash
# Clone the repository
git clone https://github.com/lokutor-ai/lokutor_tts
cd sdk/python

# Or use SSH
git clone git@github.com:lokutor-ai/lokutor_tts.git
cd sdk/python
```

### Create Virtual Environment

```bash
# Create virtual environment
python -m venv venv

# Activate virtual environment
# macOS/Linux:
source venv/bin/activate

# Windows (Command Prompt):
venv\Scripts\activate

# Windows (PowerShell):
venv\Scripts\Activate.ps1
```

### Install in Development Mode

```bash
# Install with development dependencies
pip install -e ".[dev]"

# This installs:
# - lokutor package in editable mode
# - Development tools (pytest, black, flake8)
# - All runtime dependencies
```

### Verify Installation

```bash
# Check installation
python -c "import lokutor; print(lokutor.__version__)"

# Run tests
pytest tests/

# Check code style
flake8 lokutor/

# Format code
black lokutor/
```

## Platform-Specific Setup

### macOS

#### Intel Mac

```bash
# Install Homebrew (if not installed)
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# Install Python
brew install python@3.12

# Install PortAudio (required for PyAudio)
brew install portaudio

# Create virtual environment
python3.12 -m venv venv
source venv/bin/activate

# Install package
pip install lokutor
```

#### Apple Silicon (M1/M2/M3)

```bash
# Homebrew is already compatible with Apple Silicon

# Install Python (native ARM64)
brew install python@3.12

# Install PortAudio
brew install portaudio

# Create virtual environment
python3.12 -m venv venv
source venv/bin/activate

# Install package
pip install lokutor

# If PyAudio installation fails:
pip install lokutor[audio]
# or install PyAudio separately with:
pip install --upgrade --force-reinstall --no-cache-dir pyaudio
```

### Linux (Ubuntu/Debian)

```bash
# Update package manager
sudo apt update

# Install Python and development tools
sudo apt install python3.12 python3.12-venv python3.12-dev

# Install PortAudio (required for PyAudio)
sudo apt install portaudio19-dev

# Install system audio libraries
sudo apt install libsndfile1 libsndfile1-dev

# Create virtual environment
python3.12 -m venv venv
source venv/bin/activate

# Install package
pip install lokutor

# If permission issues with audio:
# Add user to audio group
sudo usermod -a -G audio $USER
# (restart or newgrp audio to take effect)
```

### Linux (Fedora/RHEL)

```bash
# Install Python and development tools
sudo dnf install python3.12 python3.12-devel

# Install PortAudio
sudo dnf install portaudio-devel

# Install system audio libraries
sudo dnf install libsndfile-devel

# Create virtual environment
python3.12 -m venv venv
source venv/bin/activate

# Install package
pip install lokutor
```

### Windows (Command Prompt)

```bash
# Ensure Python 3.8+ is installed
python --version

# Create virtual environment
python -m venv venv

# Activate virtual environment
venv\Scripts\activate

# Upgrade pip
python -m pip install --upgrade pip

# Install package
pip install lokutor

# Verify installation
python -c "import lokutor; print(lokutor.__version__)"
```

### Windows (PowerShell)

```powershell
# Ensure Python 3.8+ is installed
python --version

# Create virtual environment
python -m venv venv

# Activate virtual environment
venv\Scripts\Activate.ps1

# If script execution policy error, run:
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

# Upgrade pip
python -m pip install --upgrade pip

# Install package
pip install lokutor

# Verify installation
python -c "import lokutor; print(lokutor.__version__)"
```

### Docker

```dockerfile
# Use official Python image
FROM python:3.12-slim

# Install system dependencies
RUN apt-get update && apt-get install -y \
    portaudio19-dev \
    libsndfile1 \
    && rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /app

# Copy application code
COPY . /app

# Create virtual environment
RUN python -m venv venv

# Activate virtual environment and install dependencies
RUN . venv/bin/activate && pip install --upgrade pip && pip install lokutor

# Default command
CMD ["venv/bin/python"]
```

Build and run:

```bash
docker build -t lokutor-app .
docker run -it --rm lokutor-app python -c "import lokutor; print(lokutor.__version__)"
```

## Verification

### Test Import

```bash
python -c "from lokutor import VoiceAgentClient, TTSClient; print('✓ Installation successful')"
```

### Test Installation

```bash
# Run quick test
python << 'EOF'
from lokutor import VoiceAgentClient, VoiceStyle, Language
from lokutor import TTSClient
from lokutor.audio import AudioRecorder, AudioPlayer

print("✓ VoiceAgentClient imported")
print("✓ TTSClient imported")
print("✓ AudioRecorder imported")
print("✓ AudioPlayer imported")
print("✓ VoiceStyle enum available")
print("✓ Language enum available")
print("✓ All components installed successfully!")
EOF
```

### Check Audio Devices

```bash
python << 'EOF'
from lokutor.audio import AudioRecorder, AudioPlayer

print("Microphone Devices:")
recorder = AudioRecorder()
recorder.list_devices()

print("\nSpeaker Devices:")
player = AudioPlayer()
player.list_devices()
EOF
```

## Troubleshooting Installation

### "ModuleNotFoundError: No module named 'pyaudio'"

**macOS:**
```bash
brew install portaudio
pip install --upgrade --force-reinstall pyaudio
```

**Linux:**
```bash
sudo apt install portaudio19-dev
pip install --upgrade --force-reinstall pyaudio
```

**Windows:**
```bash
# Try pre-compiled wheel
pip install pipwin
pipwin install pyaudio
```

### "No module named '_ctypes'"

This is a Python compilation issue. Reinstall Python:

```bash
# macOS
brew reinstall python@3.12

# Linux
sudo apt install python3.12-dev
```

### Permission Denied on Linux

```bash
# Add user to audio group
sudo usermod -a -G audio $USER

# Apply new group membership
newgrp audio

# Or restart terminal for changes to take effect
```

### Virtual Environment Issues

```bash
# Recreate virtual environment
deactivate  # Exit current venv
rm -rf venv  # Delete venv
python -m venv venv  # Create new
source venv/bin/activate  # macOS/Linux
# or
venv\Scripts\activate  # Windows

# Reinstall
pip install --upgrade pip
pip install lokutor
```

### Pip Installation Issues

```bash
# Upgrade pip
python -m pip install --upgrade pip

# Clear pip cache
pip cache purge

# Reinstall with verbose output
pip install -v lokutor
```

### Version Conflicts

```bash
# Install specific compatible versions
pip install 'websocket-client>=1.0.0,<2.0.0'
pip install 'numpy>=1.21.0,<2.0.0'
pip install 'pyaudio>=0.2.13'

# Then install lokutor
pip install lokutor
```

## Uninstallation

### Remove Package

```bash
pip uninstall lokutor
```

### Remove Virtual Environment

```bash
# macOS/Linux
deactivate
rm -rf venv

# Windows
deactivate
rmdir /s venv
```

### Clean Installation

```bash
# Complete cleanup
pip uninstall lokutor websocket-client pyaudio numpy
pip cache purge

# Remove virtual environment
deactivate
rm -rf venv

# Start fresh
python -m venv venv
source venv/bin/activate  # macOS/Linux
pip install lokutor
```

## Advanced Options

### Install with Extras

```bash
# Install with development tools
pip install lokutor[dev]

# Custom installation for specific features
pip install lokutor[audio]
```

### Editable Installation for Development

```bash
git clone https://github.com/lokutor-ai/lokutor_tts
cd sdk/python
pip install -e .

# Changes to source code are reflected immediately
```

### Install from Wheel

```bash
# Download wheel from releases
pip install lokutor-1.0.0-py3-none-any.whl
```

### Install from Source Archive

```bash
# Extract and install
tar xzf lokutor-1.0.0.tar.gz
cd lokutor-1.0.0
pip install .
```

## Environment Variables

Optional configuration via environment variables:

```bash
# Server URL (if not using default)
export LOKUTOR_SERVER_URL="ws://custom-server:8080/ws/agent"

# API key
export LOKUTOR_API_KEY="your-api-key"

# Debug logging
export LOKUTOR_DEBUG=1

# Audio settings
export LOKUTOR_SAMPLE_RATE=16000
export LOKUTOR_CHANNELS=1
```

## Next Steps

After installation:

1. **Read Quick Start**: See [README.md](README.md)
2. **Try Examples**: Run [examples/](examples/)
3. **Read API Docs**: See [API_REFERENCE.md](API_REFERENCE.md)
4. **Troubleshoot**: Check [TROUBLESHOOTING.md](TROUBLESHOOTING.md)

## Getting Help

- 📖 [Documentation](README.md)
- 🐛 [Issues](https://github.com/lokutor-ai/lokutor_tts/issues)
- 💬 [Discussions](https://github.com/lokutor-ai/lokutor_tts/discussions)
- 📧 support@lokutor.com

---

**Installation Complete!** 🎉

Ready to start using Lokutor. See [README.md](README.md) for quick start guide.
