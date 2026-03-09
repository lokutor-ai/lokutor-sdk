#!/usr/bin/env python3
"""
Advanced example with callbacks and custom configuration
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent.parent))

from lokutor import VoiceAgentClient, VoiceStyle, Language


def on_transcription(text):
    """Called when user speech is transcribed"""
    print(f"📝 You: {text}")


def on_response(text):
    """Called when agent generates response"""
    print(f"🤖 Agent: {text}")


def on_error(error):
    """Called when an error occurs"""
    print(f"⚠️  Error: {error}")


def main():
    """Advanced voice chat with callbacks"""
    
    client = VoiceAgentClient(
        api_key="your-api-key",
        prompt="""You are an expert Python developer. Help the user with:
- Code reviews and suggestions
- Debugging help
- Explanations of Python concepts
- Best practices and patterns

Keep responses concise and practical.""",
        voice=VoiceStyle.M3,  # Male voice 3
        language=Language.ENGLISH,
        # Add callbacks to track conversation
        on_transcription=on_transcription,
        on_response=on_response,
        on_error=on_error,
    )

    try:
        print("🚀 Starting Python coding assistant...")
        print("💡 Ask questions about Python, debugging, code reviews, etc.")
        print("Press Ctrl+C to exit\n")
        
        client.start_conversation()
        
    except KeyboardInterrupt:
        print("\n👋 Goodbye! Keep coding!")
    finally:
        client.disconnect()


if __name__ == "__main__":
    main()
