/**
 * Example: Using the Lokutor JavaScript SDK with Viseme Support
 * 
 * This example demonstrates how to use the Voice Agent with viseme requests
 * for animation-ready lip-sync data.
 */

import { VoiceAgentClient, Viseme, VoiceStyle, Language } from "../src/index";
import { BrowserAudioManager } from "../src/browser-audio";

class VisemeExample {
  private client!: VoiceAgentClient;
  private audioManager!: BrowserAudioManager;
  private visemesEnabled = true;

  async initialize() {
    console.log("🎙️ Lokutor Voice Agent - Viseme Example\n");

    // Initialize audio manager
    this.audioManager = new BrowserAudioManager({
      inputSampleRate: 16000,
      outputSampleRate: 24000,
    });

    // Initialize voice agent client with viseme support
    this.client = new VoiceAgentClient({
      apiKey: "your-api-key-here",
      prompt: "You are a helpful and friendly voice assistant.",
      voice: VoiceStyle.F1,
      language: Language.ENGLISH,
      visemes: true,
      onTranscription: (text) => this.onTranscription(text),
      onResponse: (text) => this.onResponse(text),
      onVisemes: (visemes) => this.onVisemeData(visemes),
      onStatus: (status) => this.onStatus(status),
      onError: (error) => this.onError(error),
    });

    // Connect to server
    if (!this.client.connect()) {
      console.error("❌ Connection failed");
      return false;
    }

    console.log("✅ Connected to Lokutor Voice Agent");
    console.log("\nText Commands:");
    console.log("  /text <message>   - Send text with visemes");
    console.log("  /visemes on/off   - Toggle viseme requests");
    console.log("  /listen           - Start listening via microphone");
    console.log("  /stop             - Stop listening");
    console.log("  /quit             - Exit\n");

    return true;
  }

  private onTranscription(text: string) {
    console.log(`\n💬 You: ${text}`);
  }

  private onResponse(text: string) {
    console.log(`\n🤖 Agent: ${text}`);
  }

  private onVisemeData(visemes: Viseme[]) {
    console.log(`\n👁️ Received ${visemes.length} visemes for animation:`);
    
    if (visemes.length <= 5) {
      // Show all if few
      visemes.forEach((v) => {
        console.log(`   [${v.timestamp.toFixed(3)}s] Viseme: ${v.char}`);
      });
    } else {
      // Show first and last if many
      console.log(`   [${visemes[0].timestamp.toFixed(3)}s] Viseme: ${visemes[0].char}`);
      console.log(`   ...`);
      const last = visemes[visemes.length - 1];
      console.log(`   [${last.timestamp.toFixed(3)}s] Viseme: ${last.char}`);
    }

    // Example: Use viseme data for animation
    this.animateVisemes(visemes);
  }

  private onStatus(status: string) {
    console.log(`\n📡 Status: ${status}`);
  }

  private onError(error: string) {
    console.error(`\n❌ Error: ${error}`);
  }

  private animateVisemes(visemes: Viseme[]) {
    /**
     * In a real application, you would:
     * 1. Convert viseme IDs to mouth shapes
     * 2. Schedule animations based on timestamps
     * 3. Sync with audio playback timeline
     * 
     * Example with hypothetical animation library:
     * 
     * visemes.forEach((viseme) => {
     *   const mouthShape = this.visemeToMouthShape(viseme.id);
     *   scheduleAnimation(mouthShape, viseme.timestamp);
     * });
     */
    console.log(`   → Ready for animation scheduling`);
  }

  async handleInput(input: string) {
    const trimmed = input.trim();

    if (!trimmed) return;

    if (trimmed.toLowerCase() === "/quit") {
      await this.cleanup();
      process.exit(0);
    } else if (trimmed.toLowerCase().startsWith("/text ")) {
      const message = trimmed.slice(6).trim();
      if (message) {
        console.log(`Sending: "${message}" (visemes=${this.visemesEnabled})`);
        this.client.send_text(message, this.visemesEnabled);
      }
    } else if (trimmed.toLowerCase() === "/visemes on") {
      this.visemesEnabled = true;
      this.client.set_visemes_enabled(true);
      console.log("✅ Visemes enabled");
    } else if (trimmed.toLowerCase() === "/visemes off") {
      this.visemesEnabled = false;
      this.client.set_visemes_enabled(false);
      console.log("✅ Visemes disabled");
    } else if (trimmed.toLowerCase() === "/listen") {
      try {
        await this.audioManager.startMicrophone();
        console.log("🎤 Microphone listening...");
      } catch (e) {
        console.error("Failed to start microphone:", e);
      }
    } else if (trimmed.toLowerCase() === "/stop") {
      this.audioManager.stopMicrophone();
      console.log("⏹️ Microphone stopped");
    } else {
      // Treat as text message
      console.log(`Sending: "${trimmed}" (visemes=${this.visemesEnabled})`);
      this.client.send_text(trimmed, this.visemesEnabled);
    }
  }

  async cleanup() {
    console.log("\n🛑 Cleaning up...");
    await this.audioManager.stopMicrophone().catch(() => {});
    this.client.disconnect();
    console.log("✅ Disconnected");
  }
}

// Main execution
async function main() {
  const example = new VisemeExample();

  if (!(await example.initialize())) {
    process.exit(1);
  }

  // Set up readline for interactive input
  const readline = require("readline");
  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
  });

  const prompt = () => {
    rl.question("> ", async (input) => {
      await example.handleInput(input);
      prompt();
    });
  };

  prompt();
}

main().catch(console.error);
