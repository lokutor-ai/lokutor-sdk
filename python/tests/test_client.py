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
        self.assertFalse(client.connected)

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
        
        self.assertTrue(client.stop_conversation)
        client.ws.close.assert_called_once()

    def test_custom_server_url(self):
        """Test custom server URL"""
        url = "ws://custom-server:9000/voice"
        client = VoiceAgentClient(
            api_key="test",
            prompt="test",
            server_url=url,
        )
        
        self.assertEqual(client.server_url, url)


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
