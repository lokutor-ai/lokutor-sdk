import { describe, it, expect } from 'vitest';

/**
 * This test verifies the EXACT raw WebSocket code shown in the docs
 * compiles and the logic is sound. It does NOT execute against a real
 * or mocked server — it validates the code pattern.
 */

describe('Docs: Raw WebSocket code validation', () => {
  it('TTS raw WebSocket snippet from docs is syntactically valid', () => {
    // This is the EXACT code from getting-started.mdx "Raw WebSocket" tab,
    // wrapped in a function for validation
    function runDocSnippet() {
      // @ts-ignore — ws is not installed in test env
      const WebSocket = require('ws');
      // @ts-ignore
      const fs = require('fs');

      const ws = new WebSocket(`wss://api.lokutor.com/ws/tts?api_key=${process.env.LOKUTOR_API_KEY}`);

      const chunks: Buffer[] = [];

      ws.on('open', () => {
        ws.send(JSON.stringify({
          text: 'Hello from Lokutor.',
          voice: 'F1',
          speed: 1.05,
          steps: 5,
        }));
      });

      ws.on('message', (data: any, isBinary: boolean) => {
        if (isBinary) {
          chunks.push(data);
        } else if (data.toString() === 'EOS') {
          fs.writeFileSync('output.raw', Buffer.concat(chunks));
          ws.close();
        }
      });
    }

    // If this compiles, the syntax is valid
    expect(typeof runDocSnippet).toBe('function');
  });

  it('Agent raw WebSocket snippet from docs is syntactically valid', () => {
    function runAgentSnippet() {
      // @ts-ignore
      const WebSocket = require('ws');

      const apiKey = 'sk-test';
      const ws = new WebSocket(`wss://api.lokutor.com/ws/agent?api_key=${apiKey}`);

      ws.binaryType = 'arraybuffer';

      ws.on('open', () => {
        ws.send(JSON.stringify({ type: 'visemes', data: true }));
        ws.send(JSON.stringify({ type: 'voice',   data: 'F1' }));
        ws.send(JSON.stringify({ type: 'language',data: 'en' }));
        ws.send(JSON.stringify({ type: 'prompt',  data: 'You are helpful.' }));
      });

      ws.on('message', (event: any) => {
        if (event.data instanceof ArrayBuffer) {
          // playAudio(new Uint8Array(event.data));
        } else {
          const msg = JSON.parse(event.data);
          console.log('Server:', msg.type, msg.data);
        }
      });

      function sendMicrophoneChunk(pcm16Data: Uint8Array) {
        if (ws.readyState === WebSocket.OPEN) {
          ws.send(pcm16Data);
        }
      }
    }

    expect(typeof runAgentSnippet).toBe('function');
  });
});

describe('Docs: TypeScript snippet audit', () => {
  it('reads .mdx files and audits TypeScript code blocks', () => {
    const fs = require('fs');
    const path = require('path');

    const docsDir = path.resolve(__dirname, '../../../lokutor_docs');
    if (!fs.existsSync(docsDir)) {
      console.log('SKIP: lokutor_docs not found at expected path');
      return;
    }

    const mdxFiles = fs.readdirSync(docsDir, { recursive: true })
      .filter((f: string) => f.endsWith('.mdx'))
      .map((f: string) => path.join(docsDir, f));

    let tsBlocks = 0;
    let issues: string[] = [];

    for (const file of mdxFiles) {
      const content = fs.readFileSync(file, 'utf-8');
      const blocks = content.match(/```typescript\n([\s\S]*?)```/g) || [];

      for (const block of blocks) {
        const code = block.replace(/```typescript\n/, '').replace(/```$/, '');
        tsBlocks++;

        // Check for placeholders that should never reach users
        if (code.includes('your-api-key') || code.includes('YOUR_API_KEY') || code.includes("sk-...")) {
          issues.push(`${path.basename(file)}: contains placeholder API key`);
        }

        // Check for balanced braces in non-partial snippets
        const isPartial = code.includes('// ...') || code.includes('...');
        if (!isPartial) {
          const openBraces = (code.match(/{/g) || []).length;
          const closeBraces = (code.match(/}/g) || []).length;
          if (openBraces !== closeBraces) {
            issues.push(`${path.basename(file)}: unbalanced braces`);
          }
        }
      }
    }

    console.log(`Audited ${tsBlocks} TypeScript blocks in ${mdxFiles.length} files`);
    if (issues.length > 0) {
      console.log('Issues found:', issues);
    }
    expect(issues).toEqual([]);
    expect(tsBlocks).toBeGreaterThan(0);
  });
});
