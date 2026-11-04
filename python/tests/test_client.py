"""
Unit tests for Lokutor Python SDK
"""

import unittest
from unittest.mock import patch, MagicMock
import json

from lokutor import VoiceAgentClient, VoiceStyle, Language


class TestVoiceAgentClient(unittest.TestCase):
    """Test VoiceAgentClient configuration and behavior"""

    def test_initialization(self):
        """Test client initializes with correct parameters"""
        client = VoiceAgentClient(
            api_key="test-key",
            prompt="Test prompt",
            voice=VoiceStyle.F1,
            language=Language.ENGLISH,
        )
        
        self.assertEqual(client.api_key, "test-key")
        self.assertEqual(client.prompt, "Test prompt")
        self.assertEqual(client.voice, VoiceStyle.F1)
        self.assertEqual(client.language, Language.ENGLISH)
        self.assertFalse(client.connected.is_set())

    def test_voice_styles(self):
        """Test all voice styles are available"""
        voices = [
            VoiceStyle.F1, VoiceStyle.F2, VoiceStyle.F3, VoiceStyle.F4, VoiceStyle.F5,
            VoiceStyle.M1, VoiceStyle.M2, VoiceStyle.M3, VoiceStyle.M4, VoiceStyle.M5,
        ]
        
        self.assertEqual(len(voices), 10)
        for voice in voices:
            self.assertIsInstance(voice, VoiceStyle)

    def test_languages(self):
        """Test all languages are available"""
        languages = [
            Language.ENGLISH, Language.SPANISH, Language.FRENCH,
            Language.GERMAN, Language.ITALIAN, Language.PORTUGUESE,
        ]
        
        self.assertEqual(len(languages), 6)
        for lang in languages:
            self.assertIsInstance(lang, Language)

    def test_callbacks(self):
        """Test callbacks are stored"""
        def dummy_callback(msg): pass
        
        client = VoiceAgentClient(
            api_key="test",
            prompt="test",
            on_transcription=dummy_callback,
            on_error=dummy_callback,
        )
        
        self.assertEqual(client.on_transcription, dummy_callback)
        self.assertEqual(client.on_error, dummy_callback)

    @patch('lokutor.client.websocket.WebSocketApp')
    def test_disconnect(self, mock_ws):
        """Test disconnect closes resources"""
        client = VoiceAgentClient(
            api_key="test",
            prompt="test",
        )
        client.ws = MagicMock()
        client.connected = True
        
        client.disconnect()
        
        self.assertFalse(client.running)
        client.ws.close.assert_called_once()


class TestVoiceStyles(unittest.TestCase):
    """Test VoiceStyle enum"""

    def test_female_voices(self):
        """Test female voice values"""
        self.assertEqual(VoiceStyle.F1.value, "F1")
        self.assertEqual(VoiceStyle.F2.value, "F2")
        self.assertEqual(VoiceStyle.F3.value, "F3")
        self.assertEqual(VoiceStyle.F4.value, "F4")
        self.assertEqual(VoiceStyle.F5.value, "F5")

    def test_male_voices(self):
        """Test male voice values"""
        self.assertEqual(VoiceStyle.M1.value, "M1")
        self.assertEqual(VoiceStyle.M2.value, "M2")
        self.assertEqual(VoiceStyle.M3.value, "M3")
        self.assertEqual(VoiceStyle.M4.value, "M4")
        self.assertEqual(VoiceStyle.M5.value, "M5")


class TestLanguages(unittest.TestCase):
    """Test Language enum"""

    def test_language_codes(self):
        """Test language codes"""
        self.assertEqual(Language.ENGLISH.value, "en")
        self.assertEqual(Language.SPANISH.value, "es")
        self.assertEqual(Language.FRENCH.value, "fr")
        self.assertEqual(Language.GERMAN.value, "de")
        self.assertEqual(Language.ITALIAN.value, "it")
        self.assertEqual(Language.PORTUGUESE.value, "pt")


if __name__ == "__main__":
    unittest.main()


class TestProtocolFixes(unittest.TestCase):
    """Wire behaviour fixed in 1.5.0, against what the server (lokutor_tts pkg/api) sends."""

    def _client(self, **kw):
        return VoiceAgentClient(api_key="k", prompt="p", **kw)

    def test_tool_call_handler_receives_call_id(self):
        got = []
        client = self._client(on_tool_call=lambda name, args, call_id: got.append((name, args, call_id)))
        client._on_message(MagicMock(), json.dumps({"type": "tool_call", "name": "book", "arguments": "{}", "call_id": "c1"}))
        self.assertEqual(got, [("book", "{}", "c1")])

    def test_two_argument_tool_handler_still_works(self):
        got = []
        client = self._client(on_tool_call=lambda name, args: got.append((name, args)))
        client._on_message(MagicMock(), json.dumps({"type": "tool_call", "name": "book", "arguments": "{}", "call_id": "c1"}))
        self.assertEqual(got, [("book", "{}")])

    def test_rates_sent_once_in_config_not_in_reply(self):
        client = self._client()
        ws = MagicMock()
        client._on_open(ws)
        sent = [json.loads(c.args[0])["type"] for c in ws.send.call_args_list]
        self.assertEqual(sent[-1], "rates")
        ws.reset_mock()
        client._on_message(ws, json.dumps({"type": "server_rates", "playback": 44100, "input": 16000}))
        ws.send.assert_not_called()

    def test_plain_string_voice_is_accepted(self):
        client = self._client(voice="clone_abc")
        ws = MagicMock()
        client._on_open(ws)
        first = json.loads(ws.send.call_args_list[0].args[0])
        self.assertEqual(first, {"type": "voice", "data": "clone_abc"})

    def _sent_language(self, **kw):
        ws = MagicMock()
        self._client(**kw)._on_open(ws)
        msgs = [json.loads(c.args[0]) for c in ws.send.call_args_list]
        return [m["data"] for m in msgs if m["type"] == "language"]

    def test_language_omitted_keeps_default(self):
        self.assertEqual(self._sent_language(), ["en"])

    def test_language_auto_or_empty_sends_empty_string(self):
        self.assertEqual(self._sent_language(language="auto"), [""])
        self.assertEqual(self._sent_language(language=""), [""])

    def test_named_language_unchanged(self):
        self.assertEqual(self._sent_language(language=Language.CATALAN), ["ca"])

    def test_clients_construct_without_pyaudio(self):
        import lokutor.client as c
        with patch.object(c, "pyaudio", None):
            from lokutor import TTSClient
            TTSClient(api_key="k")  # used to raise: the audio device was opened in the constructor
            client = self._client()
            client.disconnect()  # stopping audio that never started must not need pyaudio either


class TestKeyterms(unittest.TestCase):
    """Custom vocabulary reaches POST /stt/transcribe and /ws/stt's first message as the API takes it."""

    def _post(self, **kw):
        from lokutor.client import STTClient
        sent = {}

        class Resp:
            def __enter__(self):
                return self

            def __exit__(self, *a):
                return False

            def read(self):
                return b'{"text": "hola"}'

        def fake_urlopen(req, timeout=None):
            sent["body"] = json.loads(req.data.decode("utf-8"))
            return Resp()

        with patch("urllib.request.urlopen", fake_urlopen):
            STTClient("k").transcribe(b"\x00" * 3200, format="pcm16", sample_rate=16000, language="es", **kw)
        return sent["body"]

    def test_transcribe_sends_keyterms(self):
        self.assertEqual(self._post(keyterms=["Etxeberria", "Invisalign"])["keyterms"], ["Etxeberria", "Invisalign"])

    def test_transcribe_without_keyterms_sends_none(self):
        self.assertNotIn("keyterms", self._post())
        self.assertNotIn("keyterms", self._post(keyterms=[]))

    def test_stream_first_message_has_keyterms(self):
        from lokutor.client import SpeechToTextClient
        c = SpeechToTextClient("k", language="es", keyterms=["Etxeberria"])
        self.assertEqual(c.keyterms, ["Etxeberria"])


class TestAudioBackends(unittest.TestCase):
    """PyAudio is optional: sounddevice takes over when it is missing or broken, or you bring your own audio."""

    def test_falls_back_to_sounddevice_when_pyaudio_is_broken(self):
        import lokutor.client as c
        fake_sd = MagicMock()
        with patch.object(c, "pyaudio", MagicMock(PyAudio=MagicMock(side_effect=OSError("PY_SSIZE_T_CLEAN")))), \
                patch.object(c, "sounddevice", fake_sd):
            io = c._default_audio_io()
        self.assertIsInstance(io, c._SoundDeviceIO)

    def test_no_backend_error_names_the_way_out(self):
        import lokutor.client as c
        with patch.object(c, "pyaudio", None), patch.object(c, "sounddevice", None):
            with self.assertRaises(ImportError) as cm:
                c._default_audio_io()
        self.assertIn("lokutor[sounddevice]", str(cm.exception))
        self.assertIn("QueueAudioIO", str(cm.exception))

    def test_forced_backend(self):
        import lokutor.client as c
        with patch.dict("os.environ", {"LOKUTOR_AUDIO_BACKEND": "sounddevice"}), \
                patch.object(c, "sounddevice", MagicMock()), patch.object(c, "pyaudio", MagicMock()):
            self.assertIsInstance(c._default_audio_io(), c._SoundDeviceIO)

    def test_sounddevice_streams(self):
        import lokutor.client as c
        fake_sd = MagicMock()
        fake_sd.RawInputStream.return_value.read.return_value = (b"\x01\x00" * 4, False)
        with patch.object(c, "sounddevice", fake_sd):
            io = c._SoundDeviceIO()
            io.start_input()
            self.assertEqual(io.read(), b"\x01\x00" * 4)
            io.start_output()
            io.write(b"\x00\x00")
            io.wait_until_finished()
            io.stop()
        fake_sd.RawOutputStream.return_value.write.assert_called_with(b"\x00\x00")

    def test_custom_audio_io_gets_speech_and_feeds_microphone(self):
        from lokutor import TTSClient, VoiceAgentClient, QueueAudioIO
        got, cleared = [], []
        io = QueueAudioIO(on_audio=got.append, on_clear=lambda: cleared.append(1))
        client = VoiceAgentClient(api_key="k", audio_io=io)
        client._on_message(None, b"\x02\x00")
        client.audio.clear_output()
        self.assertEqual(got, [b"\x02\x00"])
        self.assertEqual(cleared, [1])
        client.send_audio(b"\x03\x00")
        self.assertEqual(io.read(), b"\x03\x00")
        io.stop()
        self.assertIsNone(io.read())
        self.assertIs(TTSClient(api_key="k", audio_io=io).audio._io, io)
