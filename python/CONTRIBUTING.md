# Contributing to Lokutor Python SDK

Thank you for your interest in contributing! This document provides guidelines and instructions for contributing to the Lokutor Python SDK.

## Code of Conduct

We are committed to providing a welcoming and inclusive environment. Please be respectful and constructive in all interactions.

## How to Contribute

### Reporting Bugs

Before creating a bug report, check if the issue already exists. When creating a report, include:

1. **Title**: Clear, descriptive title
2. **Description**: What you expected vs. what happened
3. **Reproduction steps**: Minimal code to reproduce
4. **Environment**:
   - Python version
   - OS and version
   - SDK version
   - Any relevant libraries
5. **Error messages**: Complete stack trace
6. **Additional context**: Screenshots, logs, etc.

**Bug Report Template:**

```
### Description
[Describe the bug]

### To Reproduce
```python
# Code to reproduce
```

### Expected Behavior
[What should happen]

### Actual Behavior
[What actually happens]

### Environment
- Python: 3.x
- OS: [macOS/Linux/Windows]
- SDK Version: 1.0.0
- Server Version: [if relevant]

### Logs
```
[Error messages and stack trace]
```
```

### Suggesting Features

1. Check if feature already exists
2. Provide clear use case and examples
3. Explain expected behavior
4. Consider backwards compatibility

**Feature Request Template:**

```
### Feature Description
[Clear description of the feature]

### Use Case
[Why this feature is needed]

### Example Usage
```python
# How the feature should work
```

### Alternatives Considered
[Other approaches you've considered]
```

## Development Workflow

### Setup Development Environment

```bash
# Clone repository
git clone https://github.com/lokutor-ai/lokutor-sdk
cd sdk/python

# Create virtual environment
python -m venv venv
source venv/bin/activate  # Windows: venv\Scripts\activate

# Install development dependencies
pip install -e ".[dev]"

# Verify setup
python -c "import lokutor; print(lokutor.__version__)"
```

### Create a Feature Branch

```bash
# Update main branch
git checkout main
git pull origin main

# Create feature branch
git checkout -b feature/my-feature
# or
git checkout -b fix/issue-123
```

**Branch naming conventions:**
- `feature/description` - New features
- `fix/description` - Bug fixes
- `docs/description` - Documentation
- `test/description` - Tests
- `refactor/description` - Code refactoring

### Make Changes

1. **Follow code style:**
   ```bash
   # Format code
   black lokutor/
   
   # Check linting
   flake8 lokutor/
   
   # Type checking (optional)
   mypy lokutor/
   ```

2. **Add tests for changes:**
   - Unit tests in `tests/test_*.py`
   - Mark with appropriate pytest markers
   - Test both success and error cases

3. **Update documentation:**
   - Update docstrings with changes
   - Update README if user-facing
   - Add examples if applicable

4. **Update CHANGELOG.md:**
   ```markdown
   ## [Unreleased]
   
   ### Added
   - New feature description
   
   ### Changed
   - Changed behavior
   
   ### Fixed
   - Bug fix description
   ```

### Run Tests Locally

```bash
# Run all tests
pytest tests/

# Run specific test file
pytest tests/test_client.py

# Run specific test
pytest tests/test_client.py::TestVoiceAgentClient::test_initialization

# Run with coverage
pytest --cov=lokutor tests/

# Run specific markers
pytest -m unit tests/        # Only unit tests
pytest -m "not slow" tests/  # Exclude slow tests
```

### Commit Changes

Write clear, descriptive commit messages:

```bash
# Good commit message
git commit -m "Add TTSClient with streaming support

- Implement synthesize() method for text-to-speech
- Add save_audio() for WAV file export
- Support streaming chunks via callbacks
- Add comprehensive docstrings

Fixes #123"

# Avoid vague messages
git commit -m "Fix stuff"  # ❌ Bad
git commit -m "Update code"  # ❌ Bad
```

### Push and Create Pull Request

```bash
# Push feature branch
git push origin feature/my-feature

# Create pull request on GitHub
# Use template below
```

**Pull Request Template:**

```markdown
## Description
[Describe what this PR does]

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Related Issues
Fixes #[issue number]

## Changes
- Change 1
- Change 2
- Change 3

## Testing
- [ ] Added unit tests
- [ ] Added integration tests
- [ ] Tests passing locally
- [ ] No test coverage regression

## Checklist
- [ ] Code follows style guidelines
- [ ] Documentation updated
- [ ] CHANGELOG.md updated
- [ ] No new warnings generated
- [ ] Backwards compatible

## Screenshots (if applicable)
[Add screenshots]

## Additional Context
[Any additional information]
```

## Code Style Guidelines

### Python Style

Follow PEP 8 with these guidelines:

```python
"""Module docstring explaining purpose and usage."""

from typing import Optional, Callable, List
import logging

logger = logging.getLogger(__name__)


class MyClass:
    """Class docstring with detailed explanation.
    
    Longer description if needed.
    
    Attributes:
        attribute: Description of attribute
    """
    
    def __init__(self, value: str) -> None:
        """Initialize the class.
        
        Args:
            value: The value to store
            
        Raises:
            ValueError: If value is invalid
        """
        if not value:
            raise ValueError("Value cannot be empty")
        self.value = value
    
    def process(
        self,
        text: str,
        callback: Optional[Callable[[str], None]] = None
    ) -> str:
        """Process text.
        
        Args:
            text: Text to process
            callback: Optional callback function
            
        Returns:
            Processed text
            
        Raises:
            ValueError: If text is invalid
        """
        if not text:
            raise ValueError("Text cannot be empty")
        
        result = text.upper()
        
        if callback:
            callback(result)
        
        return result


def standalone_function(param: str) -> int:
    """Module-level function docstring.
    
    Args:
        param: Parameter description
        
    Returns:
        Return value description
    """
    return len(param)
```

### Docstring Format

Use Google-style docstrings:

```python
def function(param1: str, param2: int = 10) -> bool:
    """One-line summary of function.
    
    Longer description if needed. Explain the purpose and
    behavior of the function.
    
    Args:
        param1: Description of param1
        param2: Description of param2. Default is 10.
        
    Returns:
        Description of return value
        
    Raises:
        ValueError: When value is invalid
        RuntimeError: When runtime error occurs
        
    Example:
        >>> result = function("test", 5)
        >>> print(result)
        True
    """
    pass
```

### Type Hints

Always use type hints:

```python
from typing import Optional, List, Dict, Callable, Union

def function(
    text: str,
    count: int = 1,
    callback: Optional[Callable[[str], None]] = None
) -> List[str]:
    """Function with complete type hints."""
    pass
```

## Testing Guidelines

### Test Organization

```
tests/
├── __init__.py
├── test_client.py         # VoiceAgentClient tests
├── test_tts_client.py     # TTSClient tests
├── test_audio.py          # AudioRecorder/AudioPlayer tests
├── test_config.py         # Configuration tests
├── test_exceptions.py     # Exception tests
└── integration/
    └── test_integration.py # Integration tests
```

### Test Structure

```python
"""Tests for module."""

import pytest
from unittest.mock import Mock, patch, MagicMock
from lokutor import VoiceAgentClient, InvalidArgumentError


@pytest.mark.unit
class TestVoiceAgentClient:
    """Test VoiceAgentClient class."""
    
    def test_initialization_success(self):
        """Test successful initialization."""
        client = VoiceAgentClient(api_key="test")
        assert client is not None
        assert not client.connected
    
    def test_initialization_invalid_key(self):
        """Test initialization with invalid key."""
        with pytest.raises(ValueError):
            VoiceAgentClient(api_key="")
    
    @patch('lokutor.client.WebSocketApp')
    def test_connection(self, mock_ws):
        """Test WebSocket connection."""
        client = VoiceAgentClient(api_key="test")
        result = client.connect()
        assert result is not None
    
    @pytest.mark.slow
    def test_real_connection(self):
        """Test real connection to server.
        
        Requires: Server running on localhost:8080
        """
        client = VoiceAgentClient(api_key="test")
        if client.connect():
            client.disconnect()


@pytest.mark.unit
class TestExceptionHandling:
    """Test exception handling."""
    
    def test_invalid_voice(self):
        """Test error on invalid voice."""
        with pytest.raises(InvalidArgumentError):
            from lokutor import VoiceAgentClient
            # Invalid voice handling
```

### Markers

Use pytest markers to categorize tests:

```python
@pytest.mark.unit
def test_something():
    """Fast unit test without external dependencies."""
    pass

@pytest.mark.integration
def test_with_server():
    """Integration test requiring server running."""
    pass

@pytest.mark.slow
def test_audio_processing():
    """Slow test that takes time."""
    pass

@pytest.mark.skip_ci
def test_interactive():
    """Skip in CI/CD, run locally only."""
    pass
```

### Mocking

Use unittest.mock for external dependencies:

```python
from unittest.mock import Mock, patch, MagicMock

@patch('lokutor.client.websocket.WebSocketApp')
def test_connection(mock_ws_app):
    """Test with mocked WebSocket."""
    mock_ws = MagicMock()
    mock_ws_app.return_value = mock_ws
    
    client = VoiceAgentClient(api_key="test")
    # Test interaction with mocked WebSocket
```

## Documentation Guidelines

### README

- Keep concise with quick start
- Include real working examples
- Add badges (Python version, license, etc.)
- Link to detailed documentation

### API Documentation

- Document all public methods
- Include parameter types and descriptions
- Provide usage examples
- Explain exceptions raised

### Comments

```python
# Good comment - explains WHY
x = x * 2  # Double value for audio mixing

# Bad comment - explains WHAT (obvious from code)
x = x * 2  # Multiply x by 2
```

## Release Process

1. **Prepare Release**
   - Update version in `setup.py`
   - Update `CHANGELOG.md`
   - Create release PR

2. **Testing**
   - Run full test suite
   - Test installation from source
   - Verify examples work

3. **Tag Release**
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

4. **Publish to PyPI**
   ```bash
   python setup.py sdist bdist_wheel
   twine upload dist/*
   ```

5. **Create GitHub Release**
   - Add release notes
   - Link to CHANGELOG entry

## Questions?

- 📖 [Documentation](README.md)
- 💬 [GitHub Discussions](https://github.com/lokutor-ai/lokutor-sdk/discussions)
- 📧 support@lokutor.com

## License

By contributing, you agree that your contributions will be licensed under the MIT License.

Thank you for contributing! 🎉
