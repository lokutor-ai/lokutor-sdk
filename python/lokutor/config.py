"""
Configuration and constants for Lokutor Voice Agent
"""

from enum import Enum


class VoiceStyle(str, Enum):
    """Available voice styles (female and male voices)"""
    # Female voices
    F1 = "F1"
    F2 = "F2"
    F3 = "F3"
    F4 = "F4"
    F5 = "F5"
    
    # Male voices
    M1 = "M1"
    M2 = "M2"
    M3 = "M3"
    M4 = "M4"
    M5 = "M5"


class Language(str, Enum):
    """Supported languages for speech and text"""
    ENGLISH = "en"
    SPANISH = "es"
    FRENCH = "fr"
    PORTUGUESE = "pt"
    KOREAN = "ko"


# Audio configuration
SAMPLE_RATE = 44100  # Standard rate for both input/output
SPEAKER_SAMPLE_RATE = 44100
CHANNELS = 1
CHUNK_DURATION_MS = 20
CHUNK_SIZE = int(SAMPLE_RATE * CHUNK_DURATION_MS / 1000)

# Default URLs
DEFAULT_VOICE_AGENT_URL = "wss://api.lokutor.com/ws/agent"
DEFAULT_TTS_URL = "wss://api.lokutor.com/ws/tts"

# Timeouts
CONNECTION_TIMEOUT = 10  # seconds
RESPONSE_TIMEOUT = 30  # seconds
