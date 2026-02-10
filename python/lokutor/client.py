"""
Main client for Lokutor Voice Agent SDK
"""

import base64
import json
import logging
import os
import time
import queue
from threading import Thread, Event
from typing import Callable, Optional

import websocket
import pyaudio

from .config import (
    VoiceStyle,
    Language,
    SAMPLE_RATE,
    SPEAKER_SAMPLE_RATE,
    CHANNELS,
    CHUNK_SIZE,
    CONNECTION_TIMEOUT,
    DEFAULT_VOICE_AGENT_URL,
    DEFAULT_TTS_URL,
)

logger = logging.getLogger(__name__)
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)

class _AudioIO:
    """Hardware abstraction for audio with background playback"""
    def __init__(self):
        self.pa = pyaudio.PyAudio()
        self.in_stream = None
        self.out_stream = None
        self.playback_queue = queue.Queue()
        self.stop_playback = Event()
        self.playback_thread = None

    def start_input(self):
        self.in_stream = self.pa.open(
            format=pyaudio.paInt16,
            channels=CHANNELS,
            rate=SAMPLE_RATE,
            input=True,
            frames_per_buffer=CHUNK_SIZE
        )

    def start_output(self):
        self.out_stream = self.pa.open(
            format=pyaudio.paInt16,
            channels=CHANNELS,
            rate=SPEAKER_SAMPLE_RATE,
            output=True
        )
        self.stop_playback.clear()
        self.playback_thread = Thread(target=self._playback_loop, daemon=True)
        self.playback_thread.start()

    def _playback_loop(self):
        """Background thread to play audio chunks without blocking WebSocket"""
        while not self.stop_playback.is_set():
            try:
                # Use a small timeout to allow checking stop_playback
                chunk = self.playback_queue.get(timeout=0.1)
                if chunk and self.out_stream:
                    self.out_stream.write(chunk)
                self.playback_queue.task_done()
            except queue.Empty:
                continue
            except Exception as e:
                logger.error(f"Playback error: {e}")
                break

    def read(self):
        return self.in_stream.read(CHUNK_SIZE, exception_on_overflow=False) if self.in_stream else None

    def write(self, data):
        """Queue audio for playback"""
        self.playback_queue.put(data)

    def clear_output(self):
        """Flush the playback queue (for interruptions)"""
        while not self.playback_queue.empty():
            try:
                self.playback_queue.get_nowait()
                self.playback_queue.task_done()
            except queue.Empty:
                break

    def stop(self):
        self.stop_playback.set()
        if self.playback_thread:
            self.playback_thread.join(timeout=1.0)
        
        for s in [self.in_stream, self.out_stream]:
            if s:
                try:
                    s.stop_stream()
                    s.close()
                except:
                    pass
        self.pa.terminate()


class VoiceAgentClient:
    """
    Simple, high-level client for voice conversations with Lokutor AI Agent
    
    Example usage:
        client = VoiceAgentClient(
            api_key="your-api-key",
            prompt="You are a helpful assistant",
            voice=VoiceStyle.F1,
            language=Language.ENGLISH
        )
        client.start_conversation()
    """

    def __init__(
        self,
        api_key: str,
        prompt: str,
        voice: VoiceStyle = VoiceStyle.F1,
        language: Language = Language.ENGLISH,
        server_url: str = DEFAULT_VOICE_AGENT_URL,
        on_transcription: Optional[Callable[[str], None]] = None,
        on_response: Optional[Callable[[str], None]] = None,
        on_error: Optional[Callable[[str], None]] = None,
    ):
        """
        Initialize Voice Agent Client
        
        Args:
            api_key: API key for authentication
            prompt: System prompt for the AI agent
            voice: Voice style to use (VoiceStyle enum)
            language: Language for speech and text (Language enum)
            server_url: WebSocket server URL
            on_transcription: Optional callback when user speech is transcribed
            on_response: Optional callback when agent text response is received
            on_error: Optional callback when errors occur
        """
        self.api_key = api_key
        self.prompt = prompt
        self.voice = voice
        self.language = language
        self.server_url = server_url
        
        # Callbacks
        self.on_transcription = on_transcription
        self.on_response = on_response
        self.on_error = on_error
        
        # Connection state
        self.ws = None
        self.connected = False
        self.stop_conversation = False
        self.audio = _AudioIO()

    def connect(self) -> bool:
        """
        Connect to the voice agent server
        
        Returns:
            True if connected successfully, False otherwise
        """
        try:
            logger.info(f"🔗 Connecting to {self.server_url}...")
            
            # Add API key to headers for authentication
            headers = []
            if self.api_key:
                headers.append(f"X-API-Key: {self.api_key}")
            
            self.ws = websocket.WebSocketApp(
                self.server_url,
                on_message=self._on_message,
                on_error=self._on_error,
                on_close=self._on_close,
                header=headers if headers else None,
            )
            self.ws.on_open = self._on_open
            
            # Run WebSocket in background thread
            ws_thread = Thread(target=self.ws.run_forever, daemon=True)
            ws_thread.start()
            
            # Wait for connection
            for i in range(CONNECTION_TIMEOUT * 2):
                time.sleep(0.5)
                if self.connected:
                    logger.info("✅ Connected to voice agent!")
                    return True
            
            logger.error(f"❌ Connection timeout after {CONNECTION_TIMEOUT}s")
            logger.error("Make sure server is running: go run cmd/server/main.go")
            return False
            
        except Exception as e:
            logger.error(f"Connection error: {e}")
            if self.on_error:
                self.on_error(f"Connection failed: {e}")
            return False

    def disconnect(self):
        """Disconnect from the server"""
        self.stop_conversation = True
        if self.ws:
            self.ws.close()
        self.audio.stop()
        logger.info("Disconnected")

    def start_conversation(self):
        """Start an interactive voice conversation"""
        if not self.connected:
            if not self.connect():
                return

        self.stop_conversation = False
        
        try:
            logger.info("🎤 Starting conversation... Speak whenever you're ready")
            self.audio.start_input()
            self.audio.start_output()
            self._run_conversation_loop()
            
        except KeyboardInterrupt:
            logger.info("\n👋 Conversation ended")
        finally:
            self.audio.stop()

    def _run_conversation_loop(self):
        """Main conversation loop - sends raw audio to server"""
        logger.debug("Starting conversation loop")
        last_pulse = time.time()
        chunks_sent = 0
        
        while not self.stop_conversation:
            chunk = self.audio.read()
            if not chunk: continue

            if self.ws and self.ws.sock and self.ws.sock.connected:
                try:
                    self.ws.send(chunk, opcode=websocket.ABNF.OPCODE_BINARY)
                    chunks_sent += 1
                    
                    # Log pulses every 2 seconds for debugging
                    if time.time() - last_pulse > 2.0:
                        logger.debug(f"Streaming pulse: sent {chunks_sent} chunks")
                        last_pulse = time.time()
                except Exception as e:
                    logger.error(f"Error sending audio: {e}")
                    break
            else:
                # If WS disconnected but loop still running, break
                if not self.ws or not self.ws.sock or not self.ws.sock.connected:
                    logger.warning("WebSocket lost - ending loop")
                    break
                time.sleep(0.1)

    def _on_open(self, ws):
        """WebSocket opened"""
        self.connected = True
        logger.info("✅ WebSocket connected")
        
        # Send initial configuration
        try:
            # Set prompt
            ws.send(json.dumps({"type": "prompt", "data": self.prompt}))
            # Set voice
            ws.send(json.dumps({"type": "voice", "data": self.voice.value}))
            # Set language
            ws.send(json.dumps({"type": "language", "data": self.language.value}))
            logger.info(f"⚙️ Configured: voice={self.voice}, language={self.language}")
        except Exception as e:
            logger.error(f"Error sending config: {e}")

    def _on_message(self, ws, message):
        """Handle WebSocket messages from server"""
        try:
            # Handle binary audio frames
            if isinstance(message, bytes):
                self.audio.write(message)
                return

            # Handle text messages (JSON)
            if isinstance(message, str):
                msg = json.loads(message)
                msg_type = msg.get("type")

                if msg_type == "audio":
                    # Backward compatibility for JSON audio
                    audio_data = base64.b64decode(msg["data"])
                    self.audio.write(audio_data)

                elif msg_type == "transcript":
                    transcript = msg.get("data")
                    role = msg.get("role", "user")
                    
                    if role == "user":
                        if self.on_transcription:
                            self.on_transcription(transcript)
                        logger.info(f"💬 You: {transcript}")
                    else:
                        if self.on_response:
                            self.on_response(transcript)
                        logger.info(f"🤖 Agent: {transcript}")

                elif msg_type == "status":
                    status = msg.get("data")
                    if status == "interrupted":
                        logger.info("⚡ Interrupted")
                        self.audio.clear_output()
                    elif status == "thinking":
                        logger.info("🧠 Thinking...")
                    elif status == "speaking":
                        logger.info("🔊 Agent speaking...")
                    elif status == "listening":
                        logger.info("👂 Listening...")

                elif msg_type == "error":
                    err_data = msg.get("data")
                    logger.error(f"❌ Server error: {err_data}")
                    if self.on_error:
                        self.on_error(err_data)

        except Exception as e:
            logger.error(f"Message handling error: {e}")

    def _on_error(self, ws, error):
        """WebSocket error"""
        logger.error(f"❌ WebSocket error: {error}")
        self.connected = False
        if self.on_error:
            self.on_error(str(error))
        self.stop_conversation = True

    def _on_close(self, ws, close_status_code, close_msg):
        """WebSocket closed"""
        logger.info("Connection closed")
        self.connected = False


class TTSClient:
    """
    Client for standalone Text-to-Speech synthesis
    """
    def __init__(
        self,
        api_key: str,
        server_url: str = DEFAULT_TTS_URL,
    ):
        self.api_key = api_key
        self.server_url = server_url
        self.audio = _AudioIO()

    def synthesize(
        self,
        text: str,
        voice: VoiceStyle = VoiceStyle.F1,
        language: Language = Language.ENGLISH,
        speed: float = 1.05,
        steps: int = 24,
        visemes: bool = False,
        on_visemes: Optional[Callable[[list], None]] = None,
        play: bool = True,
        block: bool = True
    ):
        """
        Synthesize text to speech
        
        Args:
            text: Text to synthesize
            voice: Voice style
            language: Language
            speed: Speech speed
            steps: Inference steps (quality)
            visemes: Whether to request visemes
            on_visemes: Optional callback for viseme data
            play: Whether to play the audio immediately
            block: Whether to wait for playback to finish
        """
        def on_open(ws):
            req = {
                "text": text,
                "voice": voice.value,
                "lang": language.value,
                "speed": speed,
                "steps": steps,
                "visemes": visemes
            }
            ws.send(json.dumps(req))

        def on_message(ws, message):
            if isinstance(message, bytes):
                if play:
                    self.audio.write(message)
            elif isinstance(message, str):
                try:
                    data = json.loads(message)
                    if isinstance(data, list) and on_visemes:
                        on_visemes(data)
                except:
                    logger.debug(f"TTS Server message: {message}")

        def on_error(ws, error):
            logger.error(f"TTS WebSocket error: {error}")

        if play:
            self.audio.start_output()
        
        headers = [f"X-API-Key: {self.api_key}"] if self.api_key else None
        ws = websocket.WebSocketApp(
            self.server_url,
            header=headers,
            on_open=on_open,
            on_message=on_message,
            on_error=on_error,
        )
        
        ws_thread = Thread(target=ws.run_forever, daemon=True)
        ws_thread.start()

        if play and block:
            # Wait for the playback queue to be empty
            time.sleep(1.0) # Initial wait
            while not self.audio.playback_queue.empty():
                time.sleep(0.1)
            time.sleep(0.5)
            self.audio.stop()
            ws.close()
        
        return ws


def simple_conversation(
    api_key: str,
    prompt: str = "You are a helpful AI assistant",
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
    server_url: str = DEFAULT_VOICE_AGENT_URL,
):
    """
    Quick function to start a conversation without manual setup
    """
    client = VoiceAgentClient(
        api_key=api_key,
        prompt=prompt,
        voice=voice,
        language=language,
        server_url=server_url,
    )
    client.start_conversation()


def simple_tts(
    text: str,
    api_key: str,
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
    play: bool = True,
    server_url: str = DEFAULT_TTS_URL,
):
    """
    Quick function for standalone TTS synthesis
    """
    client = TTSClient(api_key=api_key, server_url=server_url)
    client.synthesize(text=text, voice=voice, language=language, play=play, block=True)

