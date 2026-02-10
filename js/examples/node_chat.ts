/**
 * Complete Node.js example for Lokutor Voice Agent
 * 
 * To run this example, you need to install these extra dependencies:
 * npm install node-record-lpcm16 speaker dotenv
 */

import { VoiceAgentClient, VoiceStyle, Language, AUDIO_CONFIG } from '../src';
import recorder from 'node-record-lpcm16';
import Speaker from 'speaker';
import * as dotenv from 'dotenv';

dotenv.config();

async function main() {
  const apiKey = process.env.LOKUTOR_API_KEY || 'your-api-key';
  
  const client = new VoiceAgentClient({
    apiKey: apiKey,
    prompt: 'You are a helpful and friendly AI assistant. Have a natural conversation.',
    voice: VoiceStyle.F1,
    language: Language.ENGLISH,
    onTranscription: (text) => process.stdout.write(`\rUser: ${text}\n`),
    onResponse: (text) => process.stdout.write(`AI: ${text}\n`),
  });

  try {
    const connected = await client.connect();
    if (!connected) return;

    console.log('🎤 Speak whenever you\'re ready (Ctrl+C to exit)');

    // 1. Setup Microphone Recording (Input)
    const mic = recorder.record({
      sampleRate: AUDIO_CONFIG.SAMPLE_RATE,
      channels: AUDIO_CONFIG.CHANNELS,
      threshold: 0,
    });

    mic.stream().on('data', (chunk: Buffer) => {
      client.sendAudio(chunk);
    });

    // 2. Setup Speaker Playback (Output)
    const speaker = new Speaker({
      channels: AUDIO_CONFIG.CHANNELS,
      bitDepth: 16,
      sampleRate: AUDIO_CONFIG.SAMPLE_RATE,
    });

    client.onAudio((chunk: Buffer) => {
      speaker.write(chunk);
    });

  } catch (err) {
    console.error('Failed to start conversation:', err);
  }
}

main().catch(console.error);
