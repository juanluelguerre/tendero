// One Shopify collection per category of Tendero's taxonomy (phase 16, the
// store in front), so the store can be browsed as our storefront is: by
// department, then by category. Run from the repository root:
//   node tools/shopify-seed/collections.mjs
//
// Each is an automated ("smart") collection whose rule is the product type the
// seeding script gave each product — our category code — so membership is
// Shopify's to keep and nothing is assigned by hand. A department gathers the
// codes of every category under it. Titles are the taxonomy's Spanish names (the
// store's language), with the English registered as translations, and each
// collection is published to the Online Store channel. A collection whose handle
// exists is updated, so a run can be repeated.
import fs from 'node:fs';
import { connect } from './shopify.mjs';

const categories = JSON.parse(fs.readFileSync('seed/categories.sample.json', 'utf8'));
const used = new Set(JSON.parse(fs.readFileSync('seed/products.sample.json', 'utf8')).map((product) => product.product_type));
const { graphql } = await connect();

const childrenOf = (code) => categories.filter((category) => category.parent === code);
const codesUnder = (code) => [code, ...childrenOf(code).flatMap((child) => codesUnder(child.code))];
const handleOf = (name) =>
  name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

const channels = await graphql(`{ publications(first: 20) { nodes { id name } } }`);
const store = channels.publications.nodes.find((publication) => publication.name === 'Online Store');

let written = 0;
for (const category of categories) {
  const codes = codesUnder(category.code).filter((code) => used.has(code));
  if (codes.length === 0) continue;

  const handle = handleOf(category.name.es);
  const input = {
    title: category.name.es,
    handle,
    ruleSet: {
      appliedDisjunctively: true,
      rules: codes.map((code) => ({ column: 'TYPE', relation: 'EQUALS', condition: code })),
    },
  };

  const existing = await graphql(`query ($handle: String!) { collectionByIdentifier(identifier: { handle: $handle }) { id } }`, { handle });
  let id = existing.collectionByIdentifier?.id;
  if (id) {
    const answer = await graphql(
      `mutation ($input: CollectionInput!) { collectionUpdate(input: $input) { collection { id } userErrors { field message } } }`,
      { input: { id, ...input } },
    );
    refuseOn(answer.collectionUpdate.userErrors, handle);
  } else {
    const answer = await graphql(
      `mutation ($input: CollectionInput!) { collectionCreate(input: $input) { collection { id } userErrors { field message } } }`,
      { input },
    );
    refuseOn(answer.collectionCreate.userErrors, handle);
    id = answer.collectionCreate.collection.id;
  }

  const content = await graphql(`query ($id: ID!) { translatableResource(resourceId: $id) { translatableContent { key digest } } }`, { id });
  const digest = content.translatableResource.translatableContent.find((entry) => entry.key === 'title')?.digest;
  if (digest) {
    const answer = await graphql(
      `mutation ($id: ID!, $translations: [TranslationInput!]!) { translationsRegister(resourceId: $id, translations: $translations) { userErrors { message } } }`,
      { id, translations: [{ locale: 'en', key: 'title', value: category.name.en, translatableContentDigest: digest }] },
    );
    refuseOn(answer.translationsRegister.userErrors, handle);
  }

  if (store) {
    const answer = await graphql(
      `mutation ($id: ID!, $input: [PublicationInput!]!) { publishablePublish(id: $id, input: $input) { userErrors { message } } }`,
      { id, input: [{ publicationId: store.id }] },
    );
    refuseOn(answer.publishablePublish.userErrors, handle);
  }

  written++;
  console.log(`${category.code.padEnd(16)} ${handle}  (${codes.join(', ')})`);
}

console.log(`\n${written} collections written.`);

function refuseOn(errors, about) {
  if (errors?.length) throw new Error(`${about} ${JSON.stringify(errors)}`);
}
