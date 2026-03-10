#!/usr/bin/env python3
"""
Example: Using the Lokutor Python SDK with Viseme Support

This example demonstrates how to use the Voice Agent with viseme requests
for lip-sync animation or other visual feedback.
"""

from lokutor import VoiceAgentClient, VoiceStyle, Language, Viseme


def on_visemes_received(visemes):
    """Handle viseme data from server"""
    print(f"\n👁️ Received {len(visemes)} visemes:")
    for viseme in visemes:
        print(f"   ID: {viseme.id}, Char: '{viseme.char}', Time: {viseme.timestamp:.3f}s")


def on_transcription(text):
    """Handle user transcription"""
    print(f"\n💬 You: {text}")


def on_response(text):
    """Handle agent response"""
    print(f"\n🤖 Agent: {text}")


def on_status(status):
    """Handle status updates"""
    print(f"\n📡 Status: {status}")


def on_error(error):
    """Handle errors"""
    print(f"\n❌ Error: {error}")


def main():
    """Main example"""
    print("🎙️ Lokutor Voice Agent - Viseme Example\n")
    
    # Create client with viseme support
    client = VoiceAgentClient(
        api_key="your-api-key-here",
        prompt="You are a helpful and friendly voice assistant.",
        voice=VoiceStyle.F1,
        language=Language.ENGLISH,
        on_transcription=on_transcription,
        on_response=on_response,
        on_visemes=on_visemes_received,
        on_status=on_status,
        on_error=on_error,
    )
    
    # Connect to server
    if not client.connect():
        print("Connection failed")
        return
    
    print("✅ Connected to Lokutor Voice Agent")
    print("Commands:")
    print("  text <message>  - Send text with visemes")
    print("  visemes on/off  - Toggle viseme requests")
    print("  quit            - Exit\n")
    
    # Interactive loop
    try:
        visemes_enabled = True
        
        while True:
            user_input = input("> ").strip()
            
            if not user_input:
                continue
            
            if user_input.lower() == "quit":
                break
            
            elif user_input.lower().startswith("text "):
                message = user_input[5:].strip()
                if message:
                    print(f"Sending: '{message}' (visemes={visemes_enabled})")
                    client.send_text(message, visemes=visemes_enabled)
            
            elif user_input.lower() == "visemes on":
                visemes_enabled = True
                client.set_visemes_enabled(True)
                print("Visemes enabled")
            
            elif user_input.lower() == "visemes off":
                visemes_enabled = False
                client.set_visemes_enabled(False)
                print("Visemes disabled")
            
            else:
                # Treat as text message
                client.send_text(user_input, visemes=visemes_enabled)
    
    except KeyboardInterrupt:
        print("\n\nInterrupted")
    finally:
        client.disconnect()
        print("Disconnected")


if __name__ == "__main__":
    main()
