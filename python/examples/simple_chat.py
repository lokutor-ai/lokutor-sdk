#!/usr/bin/env python3
"""
Simple example of using Lokutor Voice Agent SDK
"""

import sys
from pathlib import Path
import os
try:
    from dotenv import load_dotenv
    load_dotenv()
except ImportError:
    pass

# Add parent directories to path for importing lokutor
sys.path.insert(0, str(Path(__file__).parent.parent))

from lokutor import VoiceAgentClient, VoiceStyle, Language


def main():
    """Simple voice chat example"""
    
    # Get API key from environment variable or set it manually
    api_key = os.getenv("LOKUTOR_API_KEY", "your-api-key-here")
    
    # Create client with your configuration
    client = VoiceAgentClient(
        api_key=api_key,
        prompt="You are a helpful and friendly AI assistant. Have a natural conversation.",
        voice=VoiceStyle.F1,  # Female voice 1
        language=Language.ENGLISH,
        # Defaults to wss://api.lokutor.com/ws/agent
    )

    try:
        # Start conversation - simple one-liner!
        client.start_conversation()
    except KeyboardInterrupt:
        print("\n👋 Goodbye!")
    finally:
        client.disconnect()


if __name__ == "__main__":
    main()
