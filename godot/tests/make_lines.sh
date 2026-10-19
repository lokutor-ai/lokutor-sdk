#!/bin/sh
# Speaks the test's player lines with macOS `say` and stores them as 16 kHz mono PCM16 for run_tests.gd.
set -e
cd "$(dirname "$0")"
mkdir -p lines
i=1
for line in "Hi! I'm heading into the mountains. What do you have for me?" \
            "The iron sword sounds good. I'll take it." \
            "Thanks, Brom. Goodbye!"; do
  say -v Samantha -o "lines/line$i.aiff" "$line"
  ffmpeg -loglevel error -y -i "lines/line$i.aiff" -ac 1 -ar 16000 -f s16le "lines/line$i.pcm"
  rm "lines/line$i.aiff"
  i=$((i + 1))
done
echo "wrote tests/lines/line1-3.pcm"
