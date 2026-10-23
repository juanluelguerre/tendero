// Fills the development store from Tendero's own seed (phase 16, P16-6), so the
// Shopify importer can read it back and the round trip can be compared field by
// field. Run from the repository root:
//   node tools/shopify-seed/seed.mjs [--limit N] [--delete-all]
//
// Each seed product becomes one Shopify product:
//   - its Spanish name and description (the store's language), and the English
//     ones registered as translations;
//   - its brand as the vendor, its category as Shopify's taxonomy category
//     through categories.json (the mapping a person approved) and as the
//     product type;
//   - its attributes as metafields under the `tendero` namespace, which the
//     importer reads back as attributes (spec decision 6, amended);
//   - its variants with the SKUs the catalogue already gives them
//     (B13BAG0711-OLIVE, B073WXYZ01-DEFAULT), so the import keeps them;
//   - its pictures uploaded, each one a variant's when the seed says it shows
//     that variant.
//
// A product whose handle (its item id, lowercased) already exists is skipped,
// so a run can be repeated; --delete-all empties the store first.
import fs from 'node:fs';
import path from 'node:path';
import { connect } from './shopify.mjs';

const NAMESPACE = 'tendero';
const args = process.argv.slice(2);
const limit = args.includes('--limit') ? Number(args[args.indexOf('--limit') + 1]) : Infinity;

const products = JSON.parse(fs.readFileSync('seed/products.sample.json', 'utf8')).slice(0, limit);
const categories = Object.fromEntries(
  Object.entries(JSON.parse(fs.readFileSync('tools/shopify-seed/categories.json', 'utf8'))).map(([gid, code]) => [code, gid]),
);
const { graphql } = await connect();

if (args.includes('--delete-all')) await deleteAll();

let created = 0;
let skipped = 0;
for (const product of products) {
  const handle = product.item_id.toLowerCase();
  const existing = await graphql(`query ($handle: String!) { productByIdentifier(identifier: { handle: $handle }) { id } }`, { handle });
  if (existing.productByIdentifier) {
    skipped++;
    continue;
  }

  const pictures = await upload(product);
  const id = await create(product, handle, pictures);
  await translate(id, product);
  created++;
  console.log(`${product.item_id}  ${product.name.es}`);
}

console.log(`\n${created} created, ${skipped} already there.`);

/** The seed's pictures, uploaded once each; what each one shows, in the seed's words. */
async function upload(product) {
  const pictures = [];
  for (const entry of product.images ?? []) {
    const file = typeof entry === 'string' ? entry : entry.file;
    const shows = typeof entry === 'string' ? null : (entry.shows ?? null);
    const bytes = fs.readFileSync(path.join('seed', file));
    const filename = path.basename(file);

    const staged = await graphql(
      `mutation ($input: [StagedUploadInput!]!) {
        stagedUploadsCreate(input: $input) {
          stagedTargets { url resourceUrl parameters { name value } }
          userErrors { field message }
        }
      }`,
      { input: [{ filename, mimeType: mimeOf(filename), resource: 'IMAGE', httpMethod: 'POST', fileSize: String(bytes.length) }] },
    );
    refuseOn(staged.stagedUploadsCreate.userErrors);
    const target = staged.stagedUploadsCreate.stagedTargets[0];

    const form = new FormData();
    for (const { name, value } of target.parameters) form.append(name, value);
    form.append('file', new Blob([bytes], { type: mimeOf(filename) }), filename);
    const sent = await fetch(target.url, { method: 'POST', body: form });
    if (!sent.ok) throw new Error(`Uploading ${file} answered ${sent.status}.`);

    pictures.push({ source: target.resourceUrl, shows });
  }
  return pictures;
}

async function create(product, handle, pictures) {
  const variants = product.variants?.length
    ? product.variants.map((variant) => ({ sku: `${product.item_id}-${variant.sku}`, options: variant.attributes }))
    : [{ sku: `${product.item_id}-DEFAULT`, options: { Title: 'Default Title' } }];

  const optionNames = [...new Set(variants.flatMap((variant) => Object.keys(variant.options)))];
  const productOptions = optionNames.map((name) => ({
    name,
    values: [...new Set(variants.map((variant) => variant.options[name]))].map((value) => ({ name: value })),
  }));

  // A picture that shows a variant's options is that variant's; the first
  // matching picture becomes the variant's own.
  const pictureOf = (variant) =>
    pictures.find((picture) => picture.shows && Object.entries(picture.shows).every(([key, value]) => variant.options[key] === value));

  const input = {
    handle,
    title: product.name.es,
    descriptionHtml: html(product.description?.es),
    vendor: product.brand,
    productType: product.product_type,
    category: categories[product.product_type],
    status: 'ACTIVE',
    productOptions,
    files: pictures.map((picture) => ({ originalSource: picture.source, contentType: 'IMAGE' })),
    metafields: Object.entries(product.attributes ?? {}).map(([key, value]) => ({
      namespace: NAMESPACE,
      key,
      type: 'single_line_text_field',
      value: String(value),
    })),
    variants: variants.map((variant) => ({
      sku: variant.sku,
      price: product.price.amount.toFixed(2),
      optionValues: Object.entries(variant.options).map(([optionName, name]) => ({ optionName, name })),
      ...(pictureOf(variant) ? { file: { originalSource: pictureOf(variant).source, contentType: 'IMAGE' } } : {}),
    })),
  };

  const answer = await graphql(
    `mutation ($input: ProductSetInput!) {
      productSet(input: $input, synchronous: true) {
        product { id }
        userErrors { field message }
      }
    }`,
    { input },
  );
  refuseOn(answer.productSet.userErrors, product.item_id);
  return answer.productSet.product.id;
}

/** The English name and description, registered against the Spanish ones' digests. */
async function translate(id, product) {
  const content = await graphql(
    `query ($id: ID!) { translatableResource(resourceId: $id) { translatableContent { key digest } } }`,
    { id },
  );
  const digest = Object.fromEntries(content.translatableResource.translatableContent.map((entry) => [entry.key, entry.digest]));

  const translations = [
    { locale: 'en', key: 'title', value: product.name.en, translatableContentDigest: digest.title },
    ...(product.description?.en && digest.body_html
      ? [{ locale: 'en', key: 'body_html', value: html(product.description.en), translatableContentDigest: digest.body_html }]
      : []),
  ];

  const answer = await graphql(
    `mutation ($id: ID!, $translations: [TranslationInput!]!) {
      translationsRegister(resourceId: $id, translations: $translations) { userErrors { field message } }
    }`,
    { id, translations },
  );
  refuseOn(answer.translationsRegister.userErrors, product.item_id);
}

async function deleteAll() {
  for (;;) {
    const page = await graphql(`{ products(first: 50) { nodes { id } } }`);
    if (page.products.nodes.length === 0) return;
    for (const { id } of page.products.nodes)
      refuseOn((await graphql(`mutation ($id: ID!) { productDelete(input: { id: $id }) { userErrors { field message } } }`, { id })).productDelete.userErrors);
    console.log(`deleted ${page.products.nodes.length}`);
  }
}

/** Plain text as HTML paragraphs: a blank line ends a paragraph. */
function html(text) {
  if (!text) return '';
  const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
  return text.split(/\n\s*\n/).map((paragraph) => `<p>${escape(paragraph.trim())}</p>`).join('');
}

function mimeOf(filename) {
  return { '.webp': 'image/webp', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg' }[path.extname(filename).toLowerCase()];
}

function refuseOn(errors, about = '') {
  if (errors?.length) throw new Error(`${about} ${JSON.stringify(errors)}`.trim());
}
