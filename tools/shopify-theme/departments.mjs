// The three departments' pictures for the store's collection cards (phase 16,
// the store in front): the storefront's own department illustrations
// (frontend/apps/storefront/public/img/departments/*.svg, 400×300) rendered at
// twice their size, uploaded to the store's Files and set as the image of the
// department's collection, which is what a theme's collection card shows.
// Run from the repository root, after tools/shopify-seed/collections.mjs:
//   node tools/shopify-theme/departments.mjs
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { connect } from '../shopify-seed/shopify.mjs';

// Department → its collection's handle (collections.mjs), its art, and what the art shows.
const departments = [
  { handle: 'ropa', art: 'apparel', alt: 'Clothing: a shirt on a hanger under the awning' },
  { handle: 'hogar', art: 'home', alt: 'Home: a lamp and a pan under the awning' },
  { handle: 'bolsas-y-mochilas', art: 'bags', alt: 'Bags: a backpack under the awning' },
];

const folder = path.join('artifacts', 'shopify-theme-hero');
fs.mkdirSync(folder, { recursive: true });

const { chromium } = createRequire(path.resolve('frontend/package.json'))('playwright');
const browser = await chromium.launch();
for (const department of departments) {
  const page = await browser.newPage({ viewport: { width: 400, height: 300 }, deviceScaleFactor: 2 });
  await page.goto(pathToFileURL(path.resolve(`frontend/apps/storefront/public/img/departments/${department.art}.svg`)).href);
  await page.screenshot({ path: path.join(folder, `tendero-department-${department.art}.png`) });
  await page.close();
}
await browser.close();

const { graphql } = await connect();

for (const department of departments) {
  const name = `tendero-department-${department.art}.png`;
  const bytes = fs.readFileSync(path.join(folder, name));

  const staged = await graphql(
    `mutation ($input: [StagedUploadInput!]!) {
      stagedUploadsCreate(input: $input) { stagedTargets { url resourceUrl parameters { name value } } userErrors { message } }
    }`,
    { input: [{ filename: name, mimeType: 'image/png', resource: 'IMAGE', httpMethod: 'POST', fileSize: String(bytes.length) }] },
  );
  const target = staged.stagedUploadsCreate.stagedTargets[0];
  const form = new FormData();
  for (const { name: field, value } of target.parameters) form.append(field, value);
  form.append('file', new Blob([bytes], { type: 'image/png' }), name);
  const sent = await fetch(target.url, { method: 'POST', body: form });
  if (!sent.ok) throw new Error(`Uploading ${name} answered ${sent.status}.`);

  // A collection's image takes a public URL, not the upload's: the picture is
  // registered in Files first, and its CDN address used once it is ready.
  const created = await graphql(
    `mutation ($files: [FileCreateInput!]!) { fileCreate(files: $files) { files { id } userErrors { message } } }`,
    { files: [{ originalSource: target.resourceUrl, filename: name, contentType: 'IMAGE', alt: department.alt }] },
  );
  if (created.fileCreate.userErrors.length) throw new Error(JSON.stringify(created.fileCreate.userErrors));
  let url = null;
  for (let attempt = 0; !url && attempt < 30; attempt++) {
    const file = await graphql(`query ($id: ID!) { node(id: $id) { ... on MediaImage { fileStatus image { url } } } }`, { id: created.fileCreate.files[0].id });
    url = file.node?.image?.url ?? null;
    if (!url) await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  if (!url) throw new Error(`${name} was not ready in time.`);

  const found = await graphql(`query ($handle: String!) { collectionByIdentifier(identifier: { handle: $handle }) { id } }`, { handle: department.handle });
  const id = found.collectionByIdentifier?.id;
  if (!id) throw new Error(`No collection ${department.handle}: run tools/shopify-seed/collections.mjs first.`);

  const answer = await graphql(
    `mutation ($input: CollectionInput!) { collectionUpdate(input: $input) { collection { image { url } } userErrors { field message } } }`,
    { input: { id, image: { src: url, altText: department.alt } } },
  );
  if (answer.collectionUpdate.userErrors.length) throw new Error(JSON.stringify(answer.collectionUpdate.userErrors));
  console.log(`${department.handle}: ${answer.collectionUpdate.collection.image?.url ?? '(image processing)'}`);
}
