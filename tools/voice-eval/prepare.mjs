// The author's recordings, redrawn as the WAV the shop listens to (phase 14, V-5).
//
//   node tools/voice-eval/prepare.mjs [recordings folder] [out folder]
//
// Any format a browser decodes (mp3, m4a, wav…) is decoded and resampled to
// 16 kHz mono in Chromium, with the same OfflineAudioContext the storefront's
// microphone uses (frontend/apps/storefront/src/app/voice/microphone.ts,
// monoAt16k): the engines hear what they would hear from the page, and no
// converter is added to the repository. Out: <id>.wav, 16-bit PCM, into
// artifacts/voice-eval (outside git).
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, extname, join, resolve } from "node:path";
import { createRequire } from "node:module";

const root = resolve(import.meta.dirname, "..", "..");
const { chromium } = createRequire(join(root, "frontend", "package.json"))(
  "playwright",
);
const from = resolve(
  process.argv[2] ?? join(root, "tools", "voice-eval", "recordings"),
);
const to = resolve(process.argv[3] ?? join(root, "artifacts", "voice-eval"));
const RATE = 16_000;

/** Floats in [-1, 1] as the shop's WAV: the storefront's wavOf, in Node. */
function wavOf(samples) {
  const wav = Buffer.alloc(44 + samples.length * 2);
  wav.write("RIFF", 0);
  wav.writeUInt32LE(36 + samples.length * 2, 4);
  wav.write("WAVE", 8);
  wav.write("fmt ", 12);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(RATE, 24);
  wav.writeUInt32LE(RATE * 2, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36);
  wav.writeUInt32LE(samples.length * 2, 40);
  samples.forEach((sample, i) => {
    const clipped = Math.max(-1, Math.min(1, sample));
    wav.writeInt16LE(
      Math.round(clipped < 0 ? clipped * 0x8000 : clipped * 0x7fff),
      44 + i * 2,
    );
  });
  return wav;
}

mkdirSync(to, { recursive: true });
const files = readdirSync(from)
  .filter((file) => /^(es|en)-\d\d\./.test(file))
  .sort();
const browser = await chromium.launch();
const page = await browser.newPage();
await page.setContent("<html></html>");

for (const file of files) {
  const encoded = readFileSync(join(from, file)).toString("base64");
  const samples = await page.evaluate(
    async ([base64, rate]) => {
      const bytes = Uint8Array.from(atob(base64), (c) =>
        c.charCodeAt(0),
      ).buffer;
      const decoding = new AudioContext();
      const decoded = await decoding.decodeAudioData(bytes);
      await decoding.close();
      const resampling = new OfflineAudioContext(
        1,
        Math.max(1, Math.ceil(decoded.duration * rate)),
        rate,
      );
      const source = resampling.createBufferSource();
      source.buffer = decoded;
      source.connect(resampling.destination);
      source.start();
      return Array.from((await resampling.startRendering()).getChannelData(0));
    },
    [encoded, RATE],
  );
  const id = basename(file, extname(file));
  writeFileSync(join(to, `${id}.wav`), wavOf(samples));
  console.log(`${id}: ${(samples.length / RATE).toFixed(2)} s`);
}

await browser.close();
console.log(`${files.length} recordings into ${to}`);
