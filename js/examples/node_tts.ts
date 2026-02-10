/**
 * Simple example for Lokutor Standalone TTS in Node.js
 */

import { TTSClient, VoiceStyle, Language, AUDIO_CONFIG } from '../src';
import Speaker from 'speaker';
import * as dotenv from 'dotenv';

dotenv.config();

async function main() {
  const apiKey = process.env.LOKUTOR_API_KEY || 'your-api-key';
  
  const client = new TTSClient({ apiKey });

  console.log('📢 Synthesizing...');

  // Setup Speaker Playback
  const speaker = new Speaker({
    channels: AUDIO_CONFIG.CHANNELS,
    bitDepth: 16,
    sampleRate: AUDIO_CONFIG.SAMPLE_RATE,
  });

  try {
    await client.synthesize({
      text: 'Hello! This is a test of the Lokutor standalone text-to-speech engine. How does it sound?',
      voice: VoiceStyle.F1,
      language: Language.ENGLISH,
      onAudio: (chunk) => {
        speaker.write(chunk);
      },
      onError: (err) => console.error('TTS Error:', err)
    });

    console.log('✅ Synthesis request complete. Waiting for playback to finish...');
    
  } catch (err) {
    console.error('Failed to synthesize:', err);
  }
}

main().catch(console.error);
