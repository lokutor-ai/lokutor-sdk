import { describe, it, expect } from 'vitest';
import { orbInks, PixelOrb, ConversationalPanel } from '../src/index';

// The orb's colours say whose turn it is: the agent speaks in its accent, the caller has the opposite
// colour on the wheel, and with no call the orb is the agent, waiting, in its accent.

function hue([r, g, b]: [number, number, number]): number {
  const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
  if (d === 0) return 0;
  const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return h / 6;
}
const turnDistance = (a: number, b: number) => { const d = Math.abs(a - b) % 1; return Math.min(d, 1 - d); };

describe('PixelOrb inks', () => {
  const wine: [number, number, number] = [0x9f / 255, 0x4e / 255, 0x5e / 255];
  for (const light of [false, true]) {
    const inks = orbInks(wine, light);
    const accentHue = hue(wine);
    it(`the agent speaks in the accent (${light ? 'light' : 'dark'} stage)`, () => {
      expect(turnDistance(hue(inks.agent.ink), accentHue)).toBeLessThan(0.02);
      expect(turnDistance(hue(inks.idle.paper), accentHue)).toBeLessThan(0.02);
    });
    it(`the caller's turn is the accent's opposite (${light ? 'light' : 'dark'} stage)`, () => {
      for (const state of ['listening', 'user'] as const) {
        expect(turnDistance(hue(inks[state].paper), accentHue)).toBeGreaterThan(0.48);
      }
    });
  }

  it('declares that it idles, so the panel shows it while waiting', () => {
    expect((PixelOrb as any).idles).toBe(true);
  });

  it('ConversationalPanel no longer needs a visualizer passed in', () => {
    // A type-level check as much as a runtime one: the option is optional.
    const cfg: ConstructorParameters<typeof ConversationalPanel>[0] = {
      container: {} as HTMLElement, apiKey: 'k', ConvoAgent: class {},
    };
    expect(cfg.SphereVisualizer).toBeUndefined();
  });
});
