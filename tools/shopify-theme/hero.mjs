// Renders the hero's two pictures and uploads them to the store's Files, where a
// theme's image settings can pick them (shopify://shop_images/<name>):
//   - tendero-hero.png, the background (tools/shopify-theme/hero.html);
//   - tendero-shopfront.png, the storefront's own shop front
//     (frontend/apps/storefront/public/img/hero-shopfront.svg), transparent,
//     which the hero shows as an image block lined up with the page's width.
// Rendered with the Playwright the frontend already installs; uploaded with the
// seeding script's client. A picture already there is replaced.
// Run from the repository root:
//   node tools/shopify-theme/hero.mjs
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { connect } from '../shopify-seed/shopify.mjs';

const folder = path.join('artifacts', 'shopify-theme-hero');
fs.mkdirSync(folder, { recursive: true });

const { chromium } = createRequire(path.resolve('frontend/package.json'))('playwright');
const browser = await chromium.launch();

async function render(source, out, width, height, scale, transparent, trimmed = false) {
  const page = await browser.newPage({ viewport: { width, height }, deviceScaleFactor: scale });
  await page.goto(pathToFileURL(path.resolve(source)).href);
  await page.waitForLoadState('networkidle');
  // An SVG's own margins become empty page in a layout that sizes the picture
  // by its width, so the shop front is cut to what it draws.
  const clip = trimmed
    ? await page.evaluate(() => {
        const box = document.documentElement.getBBox();
        return { x: box.x, y: box.y, width: box.width, height: box.height };
      })
    : undefined;
  await page.screenshot({ path: out, omitBackground: transparent, clip });
  await page.close();
}

const pictures = [
  {
    name: 'tendero-hero.png',
    alt: 'The page of a shop under a striped awning',
    render: (out) => render('tools/shopify-theme/hero.html', out, 2400, 1000, 1, false),
  },
  {
    name: 'tendero-shopfront.png',
    alt: "Tendero's shop front: a striped awning over a counter with a shirt, a lamp, a pan and a backpack",
    render: (out) => render('frontend/apps/storefront/public/img/hero-shopfront.svg', out, 720, 460, 2, true, true),
  },
];

for (const picture of pictures) await picture.render(path.join(folder, picture.name));
await browser.close();

const { graphql } = await connect();

for (const picture of pictures) {
  const existing = await graphql(`query ($q: String!) { files(first: 5, query: $q) { nodes { id } } }`, { q: `filename:${picture.name}` });
  for (const { id } of existing.files.nodes)
    await graphql(`mutation ($ids: [ID!]!) { fileDelete(fileIds: $ids) { userErrors { message } } }`, { ids: [id] });

  const bytes = fs.readFileSync(path.join(folder, picture.name));
  const staged = await graphql(
    `mutation ($input: [StagedUploadInput!]!) {
      stagedUploadsCreate(input: $input) { stagedTargets { url resourceUrl parameters { name value } } userErrors { message } }
    }`,
    { input: [{ filename: picture.name, mimeType: 'image/png', resource: 'IMAGE', httpMethod: 'POST', fileSize: String(bytes.length) }] },
  );
  const target = staged.stagedUploadsCreate.stagedTargets[0];
  const form = new FormData();
  for (const { name, value } of target.parameters) form.append(name, value);
  form.append('file', new Blob([bytes], { type: 'image/png' }), picture.name);
  const sent = await fetch(target.url, { method: 'POST', body: form });
  if (!sent.ok) throw new Error(`Uploading ${picture.name} answered ${sent.status}.`);

  const created = await graphql(
    `mutation ($files: [FileCreateInput!]!) { fileCreate(files: $files) { files { id } userErrors { message } } }`,
    { files: [{ originalSource: target.resourceUrl, filename: picture.name, contentType: 'IMAGE', alt: picture.alt }] },
  );
  if (created.fileCreate.userErrors.length) throw new Error(JSON.stringify(created.fileCreate.userErrors));
  console.log(`${picture.name} (${bytes.length} bytes): shopify://shop_images/${picture.name}`);
}
