import unittest
from unittest.mock import MagicMock, patch
import json
import base64
import sys
import os

# Add the sdk/python directory to path so we can import lokutor
sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..')))

from lokutor import VoiceAgentClient, TTSClient, VoiceStyle, Language

class TestSDKLogic(unittest.TestCase):
    def setUp(self):
        self.api_key = "test-key"
        self.prompt = "test-prompt"

    @patch('lokutor.client.pyaudio.PyAudio')
    @patch('lokutor.client.websocket.WebSocketApp')
    def test_voice_agent_init(self, mock_ws, mock_pa):
        client = VoiceAgentClient(
            api_key=self.api_key,
            prompt=self.prompt,
            voice=VoiceStyle.F1,
            language=Language.ENGLISH
        )
        self.assertEqual(client.api_key, self.api_key)
        self.assertEqual(client.prompt, self.prompt)
        self.assertEqual(client.voice, VoiceStyle.F1)
        self.assertEqual(client.language, Language.ENGLISH)

    @patch('lokutor.client.pyaudio.PyAudio')
    @patch('lokutor.client.websocket.WebSocketApp')
    def test_voice_agent_on_open(self, mock_ws_app, mock_pa):
        client = VoiceAgentClient(api_key=self.api_key, prompt=self.prompt)
        mock_ws = MagicMock()
        client._on_open(mock_ws)
        
        # Check if config messages were sent
        calls = mock_ws.send.call_args_list
        sent_messages = [json.loads(call[0][0]) for call in calls]
        
        self.assertTrue(any(msg.get("type") == "prompt" and msg.get("data") == self.prompt for msg in sent_messages))
        self.assertTrue(any(msg.get("type") == "voice" and msg.get("data") == VoiceStyle.F1.value for msg in sent_messages))
        self.assertTrue(any(msg.get("type") == "language" and msg.get("data") == Language.ENGLISH.value for msg in sent_messages))

    @patch('lokutor.client.pyaudio.PyAudio')
    @patch('lokutor.client.websocket.WebSocketApp')
    def test_voice_agent_on_message_binary(self, mock_ws_app, mock_pa):
        client = VoiceAgentClient(api_key=self.api_key, prompt=self.prompt)
        client.audio = MagicMock()
        
        test_audio = b"\x00\x01\x02\x03"
        client._on_message(None, test_audio)
        
        client.audio.write.assert_called_with(test_audio)

    @patch('lokutor.client.pyaudio.PyAudio')
    @patch('lokutor.client.websocket.WebSocketApp')
    def test_voice_agent_on_message_transcript(self, mock_ws_app, mock_pa):
        on_transcription = MagicMock()
        on_response = MagicMock()
        client = VoiceAgentClient(
            api_key=self.api_key, 
            prompt=self.prompt,
            on_transcription=on_transcription,
            on_response=on_response
        )
        
        # Test user transcript
        msg_user = json.dumps({"type": "transcript", "data": "hello", "role": "user"})
        client._on_message(None, msg_user)
        on_transcription.assert_called_with("hello")
        
        # Test agent transcript
        msg_agent = json.dumps({"type": "transcript", "data": "hi there", "role": "agent"})
        client._on_message(None, msg_agent)
        on_response.assert_called_with("hi there")

    @patch('lokutor.client.pyaudio.PyAudio')
    @patch('lokutor.client.websocket.WebSocketApp')
    @patch('lokutor.client.Thread')
    def test_tts_client_synthesize(self, mock_thread, mock_ws_app, mock_pa):
        client = TTSClient(api_key=self.api_key)
        
        # Mock synchronize synthesis call
        # Since synthesis starts a thread and waits, we mock the call enough to check on_open
        client.synthesize(text="hello", play=False, block=False)
        
        # Check if WebSocketApp was created with correct header
        kwargs = mock_ws_app.call_args.kwargs
        self.assertEqual(kwargs.get('header'), [f"X-API-Key: {self.api_key}"])
        
        # Check on_open behavior
        on_open_callback = kwargs.get('on_open')
        mock_ws = MagicMock()
        on_open_callback(mock_ws)
        
        sent_msg = json.loads(mock_ws.send.call_args[0][0])
        self.assertEqual(sent_msg["text"], "hello")
        self.assertEqual(sent_msg["voice"], VoiceStyle.F1.value)

    def test_config_constants(self):
        self.assertEqual(VoiceStyle.F1, "F1")
        self.assertEqual(Language.ENGLISH, "en")

if __name__ == '__main__':
    unittest.main()
