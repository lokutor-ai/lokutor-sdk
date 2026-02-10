#!/usr/bin/env python3
"""
Simple example of using Lokutor standalone TTS
"""

import sys
from pathlib import Path
import os

# Add parent directories to path for importing lokutor
sys.path.insert(0, str(Path(__file__).parent.parent))

from lokutor import TTSClient, VoiceStyle, Language

def main():
    # Get API key from environment variable or set it manually
    api_key = os.getenv("LOKUTOR_API_KEY", "your-api-key-here")
    
    # Create TTS client
    client = TTSClient(api_key=api_key)

    # Synthesize and play
    print("📢 Synthesizing...")
    client.synthesize(
        text="Hello! This is a test of the Lokutor standalone text-to-speech engine. How does it sound?",
        voice=VoiceStyle.F1,
        language=Language.ENGLISH,
        speed=1.05,
        steps=24,
        play=True,
        block=True # Wait for playback to finish
    )
    print("✅ Done")

if __name__ == "__main__":
    main()
