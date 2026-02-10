# Development Guide

Contributing to the Lokutor Python SDK

## Setup Development Environment

### Clone and Install

```bash
# Clone the repository
git clone https://github.com/lokutor-ai/lokutor_tts
cd sdk/python

# Create virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install in development mode
pip install -e ".[dev]"
```

### Requirements

- Python 3.8+
- pip / poetry
- git
- A running Lokutor server (see main README)

## Project Structure

```
lokutor/
├── __init__.py           # Main exports
├── client.py            # VoiceAgentClient class
├── tts_client.py        # TTSClient class
├── audio.py             # AudioRecorder, AudioPlayer
├── config.py            # Configuration, enums
├── exceptions.py        # Custom exceptions
└── __pycache__/

examples/
├── simple_chat.py       # Basic conversation example
├── advanced_chat.py     # Advanced features example
└── quickstart.py        # Quick start example

tests/
├── __init__.py
└── test_client.py       # Unit tests

setup.py                 # Package configuration
README.md               # Main documentation
CHANGELOG.md            # Version history
LICENSE                 # MIT License
```

## Code Style

We follow PEP 8 style guidelines:

```bash
# Check style with flake8
pip install flake8
flake8 lokutor/

# Format code with black
pip install black
black lokutor/
```

### Style Guidelines

1. **Line Length**: Max 100 characters
2. **Imports**: Group into standard lib, third-party, local
3. **Docstrings**: Google-style format
4. **Type Hints**: Use throughout (Python 3.8+)
5. **Comments**: Explain "why" not "what"

### Example Code Style

```python
"""Module docstring explaining purpose."""

from typing import Optional, Callable
import logging

logger = logging.getLogger(__name__)


class MyClass:
    """Class docstring with Google-style format.
    
    Longer explanation if needed.
    
    Attributes:
        value: Description of value.
    """
    
    def __init__(self, value: str) -> None:
        """Initialize class.
        
        Args:
            value: The value to store.
        """
        self.value = value
    
    def process(self, callback: Optional[Callable] = None) -> str:
        """Process the value.
        
        Args:
            callback: Optional callback function.
            
        Returns:
            Processed value.
            
        Raises:
            ValueError: If value is invalid.
        """
        if not self.value:
            raise ValueError("Value cannot be empty")
        
        result = self.value.upper()
        if callback:
            callback(result)
        
        return result
```

## Testing

### Run Tests

```bash
# Run all tests
pytest tests/

# Run with coverage
pytest --cov=lokutor tests/

# Run specific test
pytest tests/test_client.py::test_connection
```

### Write Tests

Create tests in `tests/test_*.py` following this pattern:

```python
"""Tests for client module."""

import pytest
from unittest.mock import Mock, patch
from lokutor import VoiceAgentClient, AuthenticationError


class TestVoiceAgentClient:
    """Test VoiceAgentClient class."""
    
    def test_initialization(self):
        """Test client initialization."""
        client = VoiceAgentClient(api_key="test-key")
        assert client is not None
        assert not client.connected
    
    def test_invalid_api_key(self):
        """Test error on invalid API key."""
        with pytest.raises(AuthenticationError):
            client = VoiceAgentClient(api_key="")
            client.connect()
    
    @patch('lokutor.client.websocket.WebSocket')
    def test_connection(self, mock_ws):
        """Test WebSocket connection."""
        client = VoiceAgentClient(api_key="test-key")
        result = client.connect()
        assert result is not None
```

## Documentation

### Docstring Format

Use Google-style docstrings:

```python
def process_audio(
    audio_data: bytes,
    sample_rate: int = 16000,
    language: str = "en"
) -> str:
    """Process audio data and return transcription.
    
    Sends audio to server for transcription using Whisper.
    
    Args:
        audio_data: Raw PCM16 audio bytes.
        sample_rate: Audio sample rate in Hz. Default 16000.
        language: Language code (en, es, fr, etc). Default 'en'.
        
    Returns:
        Transcribed text.
        
    Raises:
        AudioError: If audio data is invalid.
        ServerError: If transcription fails.
        
    Example:
        >>> audio = record_audio()
        >>> text = process_audio(audio)
        >>> print(text)
    """
    pass
```

### Update Documentation

1. **README.md**: User-facing examples and quick start
2. **CHANGELOG.md**: Track all changes per version
3. **Code Comments**: Explain complex logic
4. **Type Hints**: Use throughout for better IDE support

## Publishing

### Before Publishing

1. Update version in `setup.py`:
   ```python
   version="1.1.0"  # Update version number
   ```

2. Update `CHANGELOG.md`:
   ```markdown
   ## [1.1.0] - 2026-01-20
   
   ### Added
   - Feature description
   
   ### Changed
   - Changed item
   
   ### Fixed
   - Fix description
   ```

3. Run tests:
   ```bash
   pytest tests/
   ```

4. Build distribution:
   ```bash
   python setup.py sdist bdist_wheel
   ```

5. Check distribution:
   ```bash
   twine check dist/*
   ```

### Publish to PyPI

```bash
# Install twine
pip install twine

# Upload to test PyPI first
twine upload --repository testpypi dist/*

# Then to production
twine upload dist/*
```

## Common Tasks

### Add a New Feature

1. Create branch: `git checkout -b feature/my-feature`
2. Add code with tests
3. Update docstrings
4. Run tests: `pytest tests/`
5. Update README.md if user-facing
6. Commit: `git commit -m "Add my feature"`
7. Push: `git push origin feature/my-feature`
8. Create pull request

### Fix a Bug

1. Create branch: `git checkout -b fix/issue-123`
2. Add test that reproduces bug
3. Fix the bug
4. Verify test passes
5. Update CHANGELOG.md
6. Commit: `git commit -m "Fix issue #123"`
7. Push and create pull request

### Release New Version

1. Update version in `setup.py`
2. Update `CHANGELOG.md`
3. Run full test suite
4. Build distribution
5. Tag release: `git tag v1.1.0`
6. Push tag: `git push origin v1.1.0`
7. Publish to PyPI
8. Create GitHub release

## Debugging

### Enable Debug Logging

```python
import logging

logging.basicConfig(level=logging.DEBUG)
logger = logging.getLogger('lokutor')

client = VoiceAgentClient(api_key="key")
# Now see detailed debug output
```

### Common Issues

**WebSocket connection fails**
```python
# Check server is running
client = VoiceAgentClient(api_key="key", server_url="ws://localhost:8080/ws/agent")
if client.connect():
    print("Connected!")
else:
    print("Connection failed - check server")
```

**Audio playback issues**
```python
# Check audio device
from lokutor.audio import AudioPlayer

player = AudioPlayer()
player.list_devices()

# Test playback with silence
import numpy as np
silence = np.zeros(16000, dtype=np.int16)  # 1 second of silence
player.play_bytes(silence)
```

## Performance

### Profiling

```python
import cProfile
import pstats
from io import StringIO

# Profile your code
profiler = cProfile.Profile()
profiler.enable()

# Your code here
client = VoiceAgentClient(api_key="key")
client.start_conversation()

profiler.disable()

# Print statistics
stats = pstats.Stats(profiler, stream=StringIO())
stats.sort_stats('cumulative')
stats.print_stats(10)
```

### Memory Usage

```python
import tracemalloc

tracemalloc.start()

# Your code
client = VoiceAgentClient(api_key="key")
current, peak = tracemalloc.get_traced_memory()
print(f"Current memory: {current / 1024 / 1024:.1f} MB")
print(f"Peak memory: {peak / 1024 / 1024:.1f} MB")
```

## Resources

- [Python Packaging Guide](https://packaging.python.org/)
- [PEP 8 Style Guide](https://www.python.org/dev/peps/pep-0008/)
- [Google Python Style Guide](https://google.github.io/styleguide/pyguide.html)
- [pytest Documentation](https://docs.pytest.org/)

## Getting Help

- 📖 Read existing code and comments
- 🔍 Check GitHub issues for similar problems
- 💬 Ask in GitHub discussions
- 📧 Contact: support@lokutor.com
