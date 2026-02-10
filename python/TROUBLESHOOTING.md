# Troubleshooting Guide

Common issues and solutions for the Lokutor Python SDK.

## Connection Issues

### "Failed to connect to WebSocket"

**Symptoms:**
- `ConnectionError: Failed to connect to WebSocket`
- Connection times out
- Connection refused immediately

**Solutions:**

1. **Check server is running:**
   ```bash
   # Terminal 1 - Start the server
   cd /path/to/lokutor_tts
   go run cmd/server/main.go
   
   # Terminal 2 - Test from Python
   python
   >>> from lokutor import VoiceAgentClient
   >>> client = VoiceAgentClient(api_key="test")
   >>> client.connect()  # Should print connection info
   ```

2. **Verify server URL:**
   ```python
   # Check you're using the correct URL
   client = VoiceAgentClient(
       api_key="key",
       server_url="ws://localhost:8080/ws/agent"  # Default URL
   )
   ```

3. **Check firewall:**
   ```bash
   # macOS/Linux
   lsof -i :8080  # Check if port 8080 is in use
   
   # Windows
   netstat -ano | findstr :8080
   ```

4. **Test network connectivity:**
   ```bash
   # Check if server is reachable
   curl -i http://localhost:8080/health
   ```

5. **Enable debug logging:**
   ```python
   import logging
   logging.basicConfig(level=logging.DEBUG)
   
   from lokutor import VoiceAgentClient
   client = VoiceAgentClient(api_key="key")
   client.connect()  # See detailed debug output
   ```

### "Connection closed unexpectedly"

**Symptoms:**
- Works briefly then disconnects
- `ConnectionError` during conversation
- Server connection drops

**Solutions:**

1. **Check server logs:**
   ```bash
   # Look at server output for errors
   # Should see: "Agent connected" when client connects
   ```

2. **Verify API key:**
   ```python
   # API key must be valid
   client = VoiceAgentClient(api_key="your-actual-key")
   ```

3. **Check message format:**
   ```python
   # Ensure messages are properly formatted
   client = VoiceAgentClient(api_key="key")
   client.start_conversation()  # Uses built-in message handling
   ```

## Audio Issues

### "No microphone detected" or "No audio devices found"

**Symptoms:**
- `AudioError: No audio device available`
- Microphone not working
- Audio input fails

**Solutions:**

1. **List available audio devices:**
   ```python
   from lokutor.audio import AudioRecorder
   
   recorder = AudioRecorder()
   recorder.list_devices()
   
   # Look for your microphone in the list
   ```

2. **Check microphone is connected:**
   ```bash
   # macOS
   system_profiler SPAudioDataType | grep "Internal Microphone"
   
   # Linux
   arecord -l
   
   # Windows
   Device Manager → Sound, video and game controllers
   ```

3. **Check microphone permissions:**
   ```bash
   # macOS - Allow terminal app to use microphone
   System Preferences → Security & Privacy → Microphone
   
   # Linux - Check ALSA config
   cat /etc/asound.conf
   
   # Windows - Check app permissions
   Settings → Privacy & Security → Microphone
   ```

4. **Test microphone directly:**
   ```bash
   # macOS
   ffmpeg -f avfoundation -i ":0" -t 5 test.wav
   
   # Linux
   arecord -d 5 test.wav
   
   # Then play back
   ffplay test.wav
   ```

5. **Test with Python:**
   ```python
   from lokutor.audio import AudioRecorder
   import numpy as np
   
   recorder = AudioRecorder()
   # Should see microphone levels increasing when you speak
   print("Speak into microphone...")
   # Monitor should show values > 100 when speaking
   ```

### "Garbled or distorted audio"

**Symptoms:**
- Audio sounds like noise
- Voice is robotic or distorted
- Audio plays but sounds wrong

**Solutions:**

1. **Check input levels:**
   ```python
   from lokutor.audio import AudioRecorder
   
   recorder = AudioRecorder()
   print("Audio threshold:", recorder.SILENCE_THRESHOLD)
   print("Recording from microphone...")
   
   # Speak - should see RMS values > 100 when speaking
   ```

2. **Reduce input volume:**
   ```bash
   # macOS
   System Preferences → Sound → Input → Input volume slider
   
   # Linux
   alsamixer  # Adjust microphone level
   
   # Windows
   Settings → Sound → Volume mixer → App volume and device preferences
   ```

3. **Check for interference:**
   - Move away from other devices
   - Close other applications using audio
   - Check for electromagnetic interference near equipment

4. **Test with headphones:**
   ```python
   # Use headphones to isolate audio
   # This helps diagnose if issue is input or output
   ```

### "No audio output" or "Speakers not working"

**Symptoms:**
- Agent response doesn't play
- No sound from speakers
- `AudioError` during playback

**Solutions:**

1. **Check speaker is selected:**
   ```python
   from lokutor.audio import AudioPlayer
   
   player = AudioPlayer()
   player.list_devices()
   
   # Verify speaker is available and not muted
   ```

2. **Test speaker:**
   ```bash
   # macOS
   afplay /System/Library/Sounds/Glass.aiff
   
   # Linux
   speaker-test -t wav
   
   # Windows
   powershell -c "[System.Media.SystemSounds]::Beep.Play()"
   ```

3. **Check volume:**
   ```bash
   # macOS
   osascript -e "output volume of (get volume settings)"
   osascript -e "set volume output volume 50"
   
   # Linux
   pactl list sinks
   pactl set-sink-volume 0 50%
   ```

4. **Test with Python:**
   ```python
   import numpy as np
   from lokutor.audio import AudioPlayer
   
   # Generate 1 second of 440Hz sine wave
   sample_rate = 16000
   t = np.linspace(0, 1, sample_rate)
   audio = np.sin(2 * np.pi * 440 * t)
   audio = (audio * 32767).astype(np.int16)
   
   player = AudioPlayer()
   player.play_bytes(audio)
   
   # You should hear a beep
   ```

## Authentication Issues

### "Invalid API key"

**Symptoms:**
- `AuthenticationError: Invalid API key`
- `401 Unauthorized`
- Connection succeeds but operations fail

**Solutions:**

1. **Verify API key:**
   ```python
   api_key = "your-key-here"
   
   # Make sure it's not empty
   assert len(api_key) > 0, "API key is empty"
   
   # Check for typos
   print(f"API key: {api_key}")
   
   client = VoiceAgentClient(api_key=api_key)
   ```

2. **Check key format:**
   ```python
   # Key should be a string
   api_key = "abc123..."  # String, not bytes
   
   # If from environment variable
   import os
   api_key = os.getenv('LOKUTOR_API_KEY', '')
   assert api_key, "LOKUTOR_API_KEY not set"
   ```

3. **Verify key is active:**
   ```bash
   # Check your API key hasn't expired
   # Contact support if key needs renewal
   ```

4. **Check key permissions:**
   ```python
   # Key must have permissions for:
   # - TTS (text-to-speech)
   # - Voice agent endpoints
   # Contact support if permissions incorrect
   ```

## Language and Voice Issues

### "Invalid voice style"

**Symptoms:**
- `InvalidArgumentError: Invalid voice`
- Unexpected voice used

**Solutions:**

1. **Use valid voices:**
   ```python
   from lokutor import VoiceStyle, Language
   
   # Valid voices
   valid_voices = [
       VoiceStyle.F1, VoiceStyle.F2, VoiceStyle.F3, VoiceStyle.F4, VoiceStyle.F5,
       VoiceStyle.M1, VoiceStyle.M2, VoiceStyle.M3, VoiceStyle.M4, VoiceStyle.M5
   ]
   
   # Use one of these
   client = VoiceAgentClient(
       api_key="key",
       voice=VoiceStyle.F1
   )
   ```

2. **Check voice availability:**
   ```python
   from lokutor.config import VoiceStyle
   
   print(VoiceStyle.F1)      # female voice 1
   print(VoiceStyle.M1)      # male voice 1
   ```

### "Invalid language"

**Symptoms:**
- `InvalidArgumentError: Invalid language`
- Wrong language used

**Solutions:**

1. **Use valid languages:**
   ```python
   from lokutor import Language
   
   # Valid languages
   valid_languages = [
       Language.ENGLISH,
       Language.SPANISH,
       Language.FRENCH,
       Language.GERMAN,
       Language.ITALIAN,
       Language.PORTUGUESE
   ]
   
   # Use one of these
   client = VoiceAgentClient(
       api_key="key",
       language=Language.SPANISH
   )
   ```

2. **Check language code:**
   ```python
   from lokutor.config import Language
   
   # These are the same
   lang = Language.ENGLISH
   print(lang)  # Prints the language config
   ```

## Performance Issues

### "High CPU usage" or "Application running slowly"

**Symptoms:**
- CPU at 100%
- Application hangs
- Delayed response

**Solutions:**

1. **Profile your code:**
   ```python
   import cProfile
   
   # Profile your application
   profiler = cProfile.Profile()
   profiler.enable()
   
   # Your code here
   client = VoiceAgentClient(api_key="key")
   client.start_conversation()
   
   profiler.disable()
   profiler.print_stats()
   ```

2. **Check for infinite loops:**
   - Ensure `start_conversation()` exits properly
   - Add timeout to long operations
   - Monitor memory usage

3. **Optimize audio processing:**
   - Use appropriate buffer sizes
   - Avoid creating multiple clients in a loop
   - Reuse client instances

4. **Monitor memory:**
   ```python
   import tracemalloc
   
   tracemalloc.start()
   
   # Your code
   client = VoiceAgentClient(api_key="key")
   
   current, peak = tracemalloc.get_traced_memory()
   print(f"Memory: {current / 1024 / 1024:.1f} MB")
   ```

### "Lag in responses"

**Symptoms:**
- Delay between speaking and response
- Network seems slow
- Audio stuttering

**Solutions:**

1. **Check network:**
   ```bash
   # Test connection to server
   ping localhost  # Check latency
   
   # Measure bandwidth
   iperf3 -c localhost
   ```

2. **Check server load:**
   ```bash
   # Monitor server CPU/memory
   top  # macOS/Linux
   # Task Manager  # Windows
   ```

3. **Optimize audio buffering:**
   ```python
   from lokutor.audio import AudioRecorder, AudioPlayer
   
   # Default buffer is 4096 bytes
   # Adjust if needed for network conditions
   ```

## Server Connection Issues

### "Server error" or "500 Internal Server Error"

**Symptoms:**
- `ServerError` exceptions
- HTTP 500 errors
- Server crashes

**Solutions:**

1. **Check server logs:**
   ```bash
   # Look at Go server console output
   # Should show errors or stack traces
   ```

2. **Restart server:**
   ```bash
   # Kill existing process
   pkill -f "go run cmd/server"
   
   # Start fresh
   go run cmd/server/main.go
   ```

3. **Check server configuration:**
   ```bash
   # Verify go.mod has correct dependencies
   cat go.mod | grep lokutor-orchestrator
   
   # Should show v1.2.0 or compatible version
   ```

4. **Enable server debug logging:**
   ```bash
   # Set environment variable
   export LOKUTOR_DEBUG=1
   go run cmd/server/main.go
   ```

## Getting Help

If you encounter issues not covered here:

1. **Enable debug logging:**
   ```python
   import logging
   logging.basicConfig(level=logging.DEBUG)
   ```

2. **Collect diagnostic information:**
   ```python
   import sys
   import platform
   from lokutor import __version__
   
   print(f"Python: {sys.version}")
   print(f"Platform: {platform.system()}")
   print(f"SDK Version: {__version__}")
   ```

3. **Create minimal reproduction:**
   - Write simplest code that reproduces the issue
   - Include error messages and stack traces
   - Note your OS and Python version

4. **Contact support:**
   - 🐛 [GitHub Issues](https://github.com/lokutor-ai/lokutor_tts/issues)
   - 💬 [GitHub Discussions](https://github.com/lokutor-ai/lokutor_tts/discussions)
   - 📧 support@lokutor.com

## FAQ

**Q: Can I use the SDK without a server?**
A: No, the SDK requires a running Lokutor server (Go application). Start it with `go run cmd/server/main.go`.

**Q: Can I use the SDK on mobile?**
A: The core library is compatible with Python, but iOS/Android require different implementations.

**Q: Is there a rate limit?**
A: Depends on your API key plan. Contact support for details.

**Q: Can I run multiple conversations simultaneously?**
A: Yes, create separate `VoiceAgentClient` instances.

**Q: How do I change the system prompt during a conversation?**
A: Use `client.set_prompt("New prompt")` to update the system prompt.

**Q: How do I switch voices/languages?**
A: Use `client.set_voice(voice)` and `client.set_language(language)` methods.

**Q: Can I save audio to file?**
A: Yes, use `TTSClient.save_audio()` or capture during synthesis.

**Q: What audio formats are supported?**
A: Currently PCM16 at 16kHz. WAV files are supported for export.
