/** The rate the shop listens at (ADR 0061): what Whisper reads without converting. */
export const SAMPLE_RATE = 16_000;

/**
 * Samples of a spoken question as the WAV the shop listens to: 16 kHz, mono,
 * 16-bit PCM. Encoded here so the server needs no converter and the browser sends
 * nothing but a plain file. Samples are floats in [-1, 1]; louder is clipped.
 */
export function wavOf(samples: Float32Array): Uint8Array {
  const bytes = new Uint8Array(44 + samples.length * 2);
  const wav = new DataView(bytes.buffer);
  const write = (at: number, text: string) =>
    [...text].forEach((letter, i) => wav.setUint8(at + i, letter.charCodeAt(0)));

  write(0, 'RIFF');
  wav.setUint32(4, 36 + samples.length * 2, true);
  write(8, 'WAVE');
  write(12, 'fmt ');
  wav.setUint32(16, 16, true);
  wav.setUint16(20, 1, true); // PCM
  wav.setUint16(22, 1, true); // mono
  wav.setUint32(24, SAMPLE_RATE, true);
  wav.setUint32(28, SAMPLE_RATE * 2, true);
  wav.setUint16(32, 2, true);
  wav.setUint16(34, 16, true);
  write(36, 'data');
  wav.setUint32(40, samples.length * 2, true);

  samples.forEach((sample, i) => {
    const clipped = Math.max(-1, Math.min(1, sample));
    wav.setInt16(44 + i * 2, clipped < 0 ? clipped * 0x8000 : clipped * 0x7fff, true);
  });
  return bytes;
}
