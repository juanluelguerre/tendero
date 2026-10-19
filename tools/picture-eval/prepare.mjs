// The picture suite's photographs, redrawn as the shopper's browser will send
// them (docs/specs/camera-search.md, CS-6 and Measures): turned by their
// orientation, at most 1,536 pixels on the long side, a JPEG with no metadata.
// Redrawn in Chromium, as tools/voice-eval/prepare.mjs redraws the recordings,
// so no image library is added to the repository.
//
//   node tools/picture-eval/prepare.mjs abo      # ABO's cases, fetched by id
//   node tools/picture-eval/prepare.mjs author   # the author's originals
//
// abo:    tools/SearchEval/Objects/abo.json → artifacts/picture-eval/abo/<image>.jpg
//         (originals cached in artifacts/picture-eval/abo-original, outside git).
// author: artifacts/camera-photos/* (ignored by git: their EXIF holds a location)
//         → design/unpublished/camera-photos/<name>.jpg, the copies the suite reads.
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { basename, extname, join, resolve } from "node:path";
import { createRequire } from "node:module";

const root = resolve(import.meta.dirname, "..", "..");
const { chromium } = createRequire(join(root, "frontend", "package.json"))(
  "playwright",
);
const LONG_SIDE = 1536;
const QUALITY = 0.85;
const ABO = "https://amazon-berkeley-objects.s3.amazonaws.com/images/original/";

const which = process.argv[2];
let jobs;
if (which === "abo") {
  const { cases } = JSON.parse(
    readFileSync(join(root, "tools", "SearchEval", "Objects", "abo.json"), "utf8"),
  );
  const cache = join(root, "artifacts", "picture-eval", "abo-original");
  mkdirSync(cache, { recursive: true });
  jobs = [];
  for (const c of cases) {
    const original = join(cache, `${c.image}.jpg`);
    if (!existsSync(original)) {
      const response = await fetch(ABO + c.path);
      if (!response.ok) throw new Error(`${c.image}: ${response.status}`);
      writeFileSync(original, Buffer.from(await response.arrayBuffer()));
    }
    jobs.push({ from: original, to: join(root, "artifacts", "picture-eval", "abo", `${c.image}.jpg`) });
  }
} else if (which === "author") {
  const from = join(root, "artifacts", "camera-photos");
  jobs = readdirSync(from)
    .filter((f) => /\.(jpe?g|png|webp)$/i.test(f))
    .sort()
    .map((f) => ({
      from: join(from, f),
      to: join(root, "design", "unpublished", "camera-photos", `${basename(f, extname(f))}.jpg`),
    }));
} else {
  console.error("usage: node tools/picture-eval/prepare.mjs abo|author");
  process.exit(2);
}

const browser = await chromium.launch();
const page = await browser.newPage();
await page.setContent("<html></html>");
for (const job of jobs) {
  const encoded = readFileSync(job.from).toString("base64");
  // The page's own decoder turns the picture by its orientation; drawing it on a
  // canvas and encoding the canvas keeps the pixels and nothing else.
  const redrawn = await page.evaluate(
    async ({ encoded, longSide, quality }) => {
      const bytes = Uint8Array.from(atob(encoded), (ch) => ch.charCodeAt(0));
      const bitmap = await createImageBitmap(new Blob([bytes]), { imageOrientation: "from-image" });
      const scale = Math.min(1, longSide / Math.max(bitmap.width, bitmap.height));
      const canvas = new OffscreenCanvas(Math.round(bitmap.width * scale), Math.round(bitmap.height * scale));
      canvas.getContext("2d").drawImage(bitmap, 0, 0, canvas.width, canvas.height);
      const blob = await canvas.convertToBlob({ type: "image/jpeg", quality });
      const out = new Uint8Array(await blob.arrayBuffer());
      let binary = "";
      for (let i = 0; i < out.length; i += 0x8000) binary += String.fromCharCode(...out.subarray(i, i + 0x8000));
      return { data: btoa(binary), width: canvas.width, height: canvas.height };
    },
    { encoded, longSide: LONG_SIDE, quality: QUALITY },
  );
  mkdirSync(resolve(job.to, ".."), { recursive: true });
  writeFileSync(job.to, Buffer.from(redrawn.data, "base64"));
  console.log(`${basename(job.to)}  ${redrawn.width}x${redrawn.height}`);
}
await browser.close();
