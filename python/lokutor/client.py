"""
Main client for Lokutor Voice Agent SDK
"""

import base64
import json
import logging
import os
import time
import queue
import threading
from threading import Thread, Event
from typing import Callable, Optional

import websocket
import pyaudio

from .config import (
    VoiceStyle,
    Language,
    Viseme,
    SAMPLE_RATE,
    SPEAKER_SAMPLE_RATE,
    CHANNELS,
    CHUNK_SIZE,
    CONNECTION_TIMEOUT,
    DEFAULT_VOICE_AGENT_URL,
    DEFAULT_TTS_URL,
)

logger = logging.getLogger(__name__)

if not logger.handlers:
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
        while not self.stop_playback.is_set():
            try:
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
        self.playback_queue.put(data)

    def clear_output(self):
        while not self.playback_queue.empty():
            try:
                self.playback_queue.get_nowait()
                self.playback_queue.task_done()
            except queue.Empty:
                break

    def wait_until_finished(self):
        if self.playback_thread and self.playback_thread.is_alive():
            self.playback_queue.join()
            time.sleep(0.3)

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
    High-level client for voice conversations with Lokutor AI Agent

    Connects to the voice agent WebSocket endpoint, sends microphone audio,
    and receives synthesized speech responses with proper interruption handling.

    Example:
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
        prompt: str = "You are a helpful AI assistant",
        voice: VoiceStyle = VoiceStyle.F1,
        language: Language = Language.ENGLISH,
        visemes: bool = False,
        tools: Optional[list] = None,
        on_transcription: Optional[Callable[[str], None]] = None,
        on_response: Optional[Callable[[str], None]] = None,
        on_audio: Optional[Callable[[bytes], None]] = None,
        on_visemes: Optional[Callable[[list], None]] = None,
        on_tool_call: Optional[Callable[[str, str], None]] = None,
        on_status: Optional[Callable[[str], None]] = None,
        on_error: Optional[Callable[[str], None]] = None,
    ):
        self.api_key = api_key
        self.prompt = prompt
        self.voice = voice
        self.language = language
        self.server_url = DEFAULT_VOICE_AGENT_URL
        self.want_visemes = visemes

        self.on_transcription = on_transcription
        self.on_response = on_response
        self.on_audio = on_audio
        self.on_visemes = on_visemes
        self.on_tool_call = on_tool_call
        self.on_status = on_status
        self.on_error = on_error

        self.ws = None
        self.ws_thread = None
        self.connected = Event()
        self.config_done = Event()
        self.running = False
        self.audio = _AudioIO()
        self.tools = tools or []
        self.current_generation = 0

        self.messages = []
        self._listeners = {}

    def on(self, event: str, callback: Callable):
        if event not in self._listeners:
            self._listeners[event] = []
        self._listeners[event].append(callback)
        return self

    def _emit(self, event: str, *args, **kwargs):
        legacy_attr = f"on_{event}"
        if hasattr(self, legacy_attr):
            cb = getattr(self, legacy_attr)
            if cb:
                try:
                    cb(*args, **kwargs)
                except Exception as e:
                    logger.error(f"Error in legacy callback {legacy_attr}: {e}")

        if event in self._listeners:
            for cb in self._listeners[event]:
                try:
                    cb(*args, **kwargs)
                except Exception as e:
                    logger.error(f"Error in listener for {event}: {e}")

    def connect(self) -> bool:
        try:
            logger.info(f"Connecting to {self.server_url}...")

            headers = [f"X-API-Key: {self.api_key}"] if self.api_key else None

            self.connected.clear()
            self.config_done.clear()

            self.ws = websocket.WebSocketApp(
                self.server_url,
                on_message=self._on_message,
                on_error=self._on_error,
                on_close=self._on_close,
                header=headers if headers else None,
            )
            self.ws.on_open = self._on_open

            self.ws_thread = Thread(target=self.ws.run_forever, daemon=True)
            self.ws_thread.start()

            if not self.connected.wait(timeout=CONNECTION_TIMEOUT):
                logger.error(f"Connection timeout after {CONNECTION_TIMEOUT}s")
                return False

            logger.info("Connected to voice agent!")
            return True

        except Exception as e:
            logger.error(f"Connection error: {e}")
            if self.on_error:
                self.on_error(f"Connection failed: {e}")
            return False

    def disconnect(self):
        self.running = False
        if self.ws:
            self.ws.close()
        self.audio.stop()
        logger.info("Disconnected")

    def update_prompt(self, new_prompt: str):
        self.prompt = new_prompt
        if self.ws and self.connected.is_set():
            try:
                self.ws.send(json.dumps({"type": "prompt", "data": new_prompt}))
                logger.info(f"Updated prompt: {new_prompt[:50]}...")
            except Exception as e:
                logger.error(f"Error updating prompt: {e}")
        else:
            logger.warning("Not connected - prompt will be updated on next connection")

    def get_transcript(self) -> list:
        return self.messages.copy()

    def get_transcript_text(self) -> str:
        lines = []
        for msg in self.messages:
            role_label = "You" if msg["role"] == "user" else "Agent"
            lines.append(f"{role_label}: {msg['text']}")
        return "\n".join(lines)

    def start_conversation(self):
        if not self.connected.is_set():
            if not self.connect():
                return

        self.running = True

        try:
            logger.info("Starting conversation... Speak whenever you're ready")
            self.audio.start_input()
            self.audio.start_output()

            self.config_done.wait(timeout=5.0)

            self._run_conversation_loop()

        except KeyboardInterrupt:
            logger.info("Conversation ended")
        finally:
            self.audio.stop()

    def _run_conversation_loop(self):
        logger.debug("Starting conversation loop")
        last_pulse = time.time()
        chunks_sent = 0

        while self.running:
            chunk = self.audio.read()
            if not chunk:
                continue

            if self.ws and self.ws.sock and self.ws.sock.connected:
                try:
                    self.ws.send(chunk, opcode=websocket.ABNF.OPCODE_BINARY)
                    chunks_sent += 1

                    if time.time() - last_pulse > 2.0:
                        logger.debug(f"Streaming pulse: sent {chunks_sent} chunks")
                        last_pulse = time.time()
                except Exception as e:
                    logger.error(f"Error sending audio: {e}")
                    break
            else:
                if not self.ws or not self.ws.sock or not self.ws.sock.connected:
                    logger.warning("WebSocket lost - ending loop")
                    break
                time.sleep(0.1)

    def _on_open(self, ws):
        logger.info("WebSocket opened (sending config...)")

        try:
            ws.send(json.dumps({"type": "voice", "data": self.voice.value}))
            ws.send(json.dumps({"type": "language", "data": self.language.value}))
            if self.tools:
                ws.send(json.dumps({"type": "tools", "data": self.tools}))
            ws.send(json.dumps({"type": "visemes", "data": self.want_visemes}))
            ws.send(json.dumps({"type": "prompt", "data": self.prompt}))

            logger.info(f"Configured: voice={self.voice.value}, language={self.language.value}, "
                        f"visemes={self.want_visemes}, tools={len(self.tools)}")

        except Exception as e:
            logger.error(f"Error sending config: {e}")

        self.connected.set()

    def _on_message(self, ws, message):
        try:
            if isinstance(message, bytes):
                self._emit("audio", message)
                self.audio.write(message)
                return

            if isinstance(message, str):
                msg = json.loads(message)
                msg_type = msg.get("type")

                if msg_type == "audio":
                    gen = msg.get("generation", 0)
                    if gen < self.current_generation:
                        return
                    data_str = msg.get("data")
                    if data_str:
                        audio_data = base64.b64decode(data_str)
                        self._emit("audio", audio_data)
                        self.audio.write(audio_data)
                    return

                elif msg_type == "transcript":
                    transcript = msg.get("data") or msg.get("text", "")
                    role = msg.get("role", "user")

                    self.messages.append({
                        "role": role,
                        "text": transcript,
                        "timestamp": time.time()
                    })

                    if role == "user":
                        self._emit("transcription", transcript)
                        logger.info(f"You: {transcript}")
                    else:
                        self._emit("response", transcript)
                        logger.info(f"Agent: {transcript}")

                elif msg_type == "status":
                    status = msg.get("data")
                    self._emit("status", status)

                    if status == "prompt_set":
                        self.config_done.set()
                        logger.debug("Config confirmed by server")

                    elif status == "interrupted":
                        logger.info("Interrupted")
                        self.audio.clear_output()

                    elif status == "thinking":
                        new_gen = msg.get("generation", 0)
                        if new_gen > self.current_generation:
                            self.current_generation = new_gen
                            self.audio.clear_output()
                        logger.info(f"Thinking... (Gen {self.current_generation})")

                    elif status == "speaking":
                        logger.info("Agent speaking...")

                    elif status == "listening":
                        logger.info("Listening...")

                    elif status == "connected":
                        pass

                elif msg_type == "server_rates":
                    playback = msg.get("playback", 44100)
                    inp = msg.get("input", 16000)
                    ws.send(json.dumps({
                        "type": "rates",
                        "playback": playback,
                        "input": inp,
                    }))
                    logger.debug(f"Configured AEC rates: playback={playback}, input={inp}")

                elif msg_type == "visemes":
                    viseme_data = msg.get("data", [])
                    vis_objs = [
                        Viseme(id=v.get("v"), char=v.get("c"), timestamp=v.get("t"))
                        for v in viseme_data
                    ]
                    self._emit("visemes", vis_objs)
                    logger.debug(f"Received {len(vis_objs)} visemes")

                elif msg_type == "tool_call":
                    name = msg.get("name")
                    args = msg.get("arguments")
                    self._emit("tool_call", name, args)
                    logger.info(f"Tool Call: {name}({args})")

                elif msg_type == "error":
                    err_data = msg.get("data")
                    logger.error(f"Server error: {err_data}")
                    self._emit("error", err_data)

        except Exception as e:
            logger.error(f"Message handling error: {e}")

    def _on_error(self, ws, error):
        logger.error(f"WebSocket error: {error}")
        if self.on_error:
            self.on_error(str(error))

    def _on_close(self, ws, close_status_code, close_msg):
        logger.info("Connection closed")
        self.connected.clear()
        self.config_done.clear()
        self.running = False


class TTSClient:
    """
    Client for standalone Text-to-Speech synthesis
    """
    def __init__(self, api_key: str):
        self.api_key = api_key
        self.server_url = DEFAULT_TTS_URL
        self.audio = _AudioIO()

    def synthesize(
        self,
        text: str,
        voice: VoiceStyle = VoiceStyle.F1,
        language: Language = Language.ENGLISH,
        speed: float = 1.05,
        steps: int = 24,
        visemes: bool = False,
        on_audio: Optional[Callable[[bytes], None]] = None,
        on_visemes: Optional[Callable[[list], None]] = None,
        on_ttfb: Optional[Callable[[float], None]] = None,
        play: bool = True,
        block: bool = True
    ):
        finished_event = Event()
        state = {"start_time": 0.0, "first_byte_received": False}

        def on_open(ws):
            req = {
                "text": text,
                "voice": voice.value,
                "lang": language.value,
                "speed": speed,
                "steps": steps,
                "visemes": visemes,
            }
            ws.send(json.dumps(req))
            state["start_time"] = time.time()

        def on_message(ws, message):
            if isinstance(message, bytes):
                if not state["first_byte_received"]:
                    ttfb = (time.time() - state["start_time"]) * 1000
                    if on_ttfb:
                        on_ttfb(ttfb)
                    state["first_byte_received"] = True
                if on_audio:
                    on_audio(message)
                if play:
                    self.audio.write(message)
            elif isinstance(message, str):
                if message == "EOS":
                    ws.close()
                    return
                try:
                    data = json.loads(message)
                    if isinstance(data, list) and on_visemes:
                        on_visemes(data)
                    if isinstance(data, dict) and data.get("type") == "eos":
                        ws.close()
                except json.JSONDecodeError:
                    logger.debug(f"TTS Server message: {message}")
                except Exception as e:
                    logger.error(f"Error handling message: {e}")

        def on_error(ws, error):
            logger.error(f"TTS WebSocket error: {error}")
            finished_event.set()

        def on_close(ws, *args):
            logger.debug("TTS connection closed")
            finished_event.set()

        if play:
            self.audio.start_output()

        headers = [f"X-API-Key: {self.api_key}"] if self.api_key else None
        ws = websocket.WebSocketApp(
            self.server_url,
            header=headers,
            on_open=on_open,
            on_message=on_message,
            on_error=on_error,
            on_close=on_close,
        )

        ws_thread = Thread(target=ws.run_forever, daemon=True)
        ws_thread.start()

        if block:
            finished_event.wait(timeout=30.0)
            if play:
                self.audio.wait_until_finished()
                self.audio.stop()
            ws.close()

        return ws


def simple_conversation(
    api_key: str,
    prompt: str = "You are a helpful AI assistant",
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
):
    client = VoiceAgentClient(
        api_key=api_key,
        prompt=prompt,
        voice=voice,
        language=language,
    )
    client.start_conversation()


def simple_tts(
    text: str,
    api_key: str,
    voice: VoiceStyle = VoiceStyle.F1,
    language: Language = Language.ENGLISH,
    play: bool = True,
):
    client = TTSClient(api_key=api_key)
    client.synthesize(text=text, voice=voice, language=language, play=play, block=True)
