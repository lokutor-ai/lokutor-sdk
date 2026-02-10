# Troubleshooting

## Connection Errors

### WebSocket connection failed
- Verify your `apiKey` is correct.
- Check if your firewall allows connections on port 443.
- If using a local server, ensure it is running and accessible at the provided `serverUrl`.

## Audio Issues

### AI voice is choppy or distorted
- This is often caused by network jitter. Ensure you have a stable internet connection.
- Check your buffer window. The server expects audio in 20ms chunks.

### No audio playback (Node.js)
- Ensure your `Speaker` settings match the `AUDIO_CONFIG`.
- Check if another application is using the audio device.

---

For more help, please open an issue on the GitHub repository or contact us at support@lokutor.com.
