#!/usr/bin/env python3
"""The player's lines for the forge video, spoken by Lokutor TTS (POST /tts/synthesize -> 44.1 kHz WAV).
Needs LOKUTOR_API_KEY. Writes demo/forge/lines/line1.wav..line3.wav."""
import json, os, pathlib, urllib.request

LINES = [
    "Hey Brom! I'm heading up the mountain pass tonight. What should I take?",
    "Then I'll take the iron sword. And a healing potion, just in case.",
    "Perfect. Thanks, Brom! See you when I'm back.",
]
VOICE = os.environ.get("PLAYER_VOICE", "F2")

key = os.environ["LOKUTOR_API_KEY"]
out = pathlib.Path(__file__).with_name("lines")
out.mkdir(exist_ok=True)
for i, text in enumerate(LINES, 1):
    req = urllib.request.Request(
        "https://api.lokutor.com/tts/synthesize",
        data=json.dumps({"text": text, "voice": VOICE, "language": "en"}).encode(),
        headers={"X-API-Key": key, "Content-Type": "application/json", "User-Agent": "lokutor-forge-demo/0.1"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=60) as r:
        wav = r.read()
    (out / f"line{i}.wav").write_bytes(wav)
    print(f"line{i}.wav  {len(wav) / 88200:.1f} s  {text}")
