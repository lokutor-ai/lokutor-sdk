"""
Configuration and constants for Lokutor Voice Agent
"""

from enum import Enum
from dataclasses import dataclass


class VoiceStyle(str, Enum):
    """Built-in voices.

    Versa 2.0 names each voice for the language its reference speaker was recorded in: EN_* are
    English speakers, ESCA_* Spanish/Catalan. That describes how a voice sounds, not what it can
    say — the model is zero-shot, so any voice can speak any supported language.

    The bare F1-M5 values are the Versa 1.x names. They still work: the server maps them onto the
    matching Versa 2.0 voice, keeping the language family of the call. Prefer the explicit names.

    This is a convenience, not the full set: a cloned voice's id ("clone_...") is also a valid
    voice, and every field that takes a voice accepts a plain str.
    """
    # English reference speakers
    EN_F1 = "en_f1"
    EN_F2 = "en_f2"
    EN_F3 = "en_f3"
    EN_F4 = "en_f4"
    EN_F5 = "en_f5"
    EN_M1 = "en_m1"
    EN_M2 = "en_m2"
    EN_M3 = "en_m3"
    EN_M4 = "en_m4"
    EN_M5 = "en_m5"

    # Spanish / Catalan reference speakers
    ESCA_F1 = "esca_f1"
    ESCA_F2 = "esca_f2"
    ESCA_F3 = "esca_f3"
    ESCA_F4 = "esca_f4"
    ESCA_F5 = "esca_f5"
    ESCA_M1 = "esca_m1"
    ESCA_M2 = "esca_m2"
    ESCA_M3 = "esca_m3"
    ESCA_M4 = "esca_m4"
    ESCA_M5 = "esca_m5"

    # Deprecated: Versa 1.x names, mapped server-side.
    F1 = "F1"
    F2 = "F2"
    F3 = "F3"
    F4 = "F4"
    F5 = "F5"
    M1 = "M1"
    M2 = "M2"
    M3 = "M3"
    M4 = "M4"
    M5 = "M5"


class Language(str, Enum):
    """Languages Lokutor supports.

    Nine, as of the Versa 2.0 rollout. The previous list carried 32 entries -- Japanese, Chinese,
    Arabic, Russian and more -- that no shipped model was trained to speak, and omitted Catalan,
    Galician and Basque, which it speaks well. Every one of these except English has a trained
    language token in the model; English is its unmarked base case.

    GET /languages serves the live list; prefer it over hardcoding if you support user choice.
    """
    ENGLISH = "en"
    SPANISH = "es"
    CATALAN = "ca"
    GALICIAN = "gl"
    BASQUE = "eu"
    PORTUGUESE = "pt"
    FRENCH = "fr"
    ITALIAN = "it"
    GERMAN = "de"


@dataclass
class Viseme:
    """Viseme data for lip-sync animation"""
    id: int
    char: str
    timestamp: float


# Audio configuration
SAMPLE_RATE = 16000  # Input (microphone) sample rate
SPEAKER_SAMPLE_RATE = 44100  # Output (speaker) sample rate
CHANNELS = 1
CHUNK_DURATION_MS = 20
CHUNK_SIZE = int(SAMPLE_RATE * CHUNK_DURATION_MS / 1000)

# Default URLs
DEFAULT_VOICE_AGENT_URL = "wss://api.lokutor.com/ws/agent"
DEFAULT_TTS_URL = "wss://api.lokutor.com/ws/tts"
DEFAULT_STT_URL = "wss://api.lokutor.com/ws/stt"

# Timeouts
CONNECTION_TIMEOUT = 10  # seconds
RESPONSE_TIMEOUT = 30  # seconds
