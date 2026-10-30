import { describe, expect, it } from 'vitest';
import { wavOf } from './wav';

const view = (bytes: Uint8Array) => new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
const text = (bytes: Uint8Array, at: number) => String.fromCharCode(...bytes.slice(at, at + 4));

/**
 * What the shop listens to (ADR 0061): a WAV of 16 kHz, mono, 16-bit PCM,
 * encoded here from the microphone's samples, so neither side needs a converter.
 */
describe('a question as a WAV', () => {
  it('writes the header the server reads: RIFF, 16 kHz, mono, 16-bit PCM, and the samples', () => {
    const wav = wavOf(new Float32Array(16_000));

    expect([
      text(wav, 0),
      text(wav, 8),
      text(wav, 12),
      view(wav).getUint16(20, true),
      view(wav).getUint16(22, true),
      view(wav).getUint32(24, true),
      view(wav).getUint16(34, true),
      text(wav, 36),
      view(wav).getUint32(40, true),
      wav.length,
    ]).toEqual(['RIFF', 'WAVE', 'fmt ', 1, 1, 16_000, 16, 'data', 32_000, 44 + 32_000]);
  });

  /** The sizes a reader trusts to find the samples, little-endian like every field of a WAV. */
  it('writes the sizes and rates the header promises', () => {
    const wav = wavOf(new Float32Array(16_000));

    expect([
      view(wav).getUint32(4, true),
      view(wav).getUint32(16, true),
      view(wav).getUint32(28, true),
      view(wav).getUint16(32, true),
    ]).toEqual([36 + 32_000, 16, 32_000, 2]);
  });

  it('turns each sample into a signed 16-bit value, and clips what is louder than full scale', () => {
    const wav = wavOf(new Float32Array([0, 0.5, -1, 2, -2]));
    const samples = [0, 1, 2, 3, 4].map((i) => view(wav).getInt16(44 + i * 2, true));

    expect(samples).toEqual([0, 16_383, -32_768, 32_767, -32_768]);
  });
});
