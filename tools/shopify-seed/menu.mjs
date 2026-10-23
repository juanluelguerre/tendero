// The store's main menu as our storefront's: Home, then each department with its
// sections and categories beneath, every item a collection written by
// collections.mjs (phase 16, the store in front). Needs
// `write_online_store_navigation`. Run from the repository root, after
// collections.mjs:
//   node tools/shopify-seed/menu.mjs
//
// Shopify's menus nest three levels, which is our taxonomy's depth: department,
// section, category. Titles are the taxonomy's Spanish names (the store's
// language), with the English registered as translations.
import fs from 'node:fs';
import { connect } from './shopify.mjs';

const categories = JSON.parse(fs.readFileSync('seed/categories.sample.json', 'utf8'));
const used = new Set(JSON.parse(fs.readFileSync('seed/products.sample.json', 'utf8')).map((product) => product.product_type));
const { graphql } = await connect();

const handleOf = (name) =>
  name.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const childrenOf = (code) => categories.filter((category) => category.parent === code);
const sells = (code) => used.has(code) || childrenOf(code).some((child) => sells(child.code));

// The collections collections.mjs wrote, by handle.
const collections = new Map();
let cursor = null;
do {
  const page = await graphql(
    `query ($after: String) { collections(first: 100, after: $after) { nodes { id handle } pageInfo { hasNextPage endCursor } } }`,
    { after: cursor },
  );
  for (const collection of page.collections.nodes) collections.set(collection.handle, collection.id);
  cursor = page.collections.pageInfo.hasNextPage ? page.collections.pageInfo.endCursor : null;
} while (cursor);

/** A category as a menu item, with the categories under it that the shop sells. */
function itemOf(category) {
  const id = collections.get(handleOf(category.name.es));
  if (!id) throw new Error(`No collection for ${category.code}: run collections.mjs first.`);
  return {
    title: category.name.es,
    type: 'COLLECTION',
    resourceId: id,
    items: childrenOf(category.code).filter((child) => sells(child.code)).map(itemOf),
  };
}

const departments = categories.filter((category) => !category.parent && sells(category.code));
const items = [{ title: 'Inicio', type: 'FRONTPAGE', items: [] }, ...departments.map(itemOf)];

const menus = await graphql(`{ menus(first: 20) { nodes { id handle title } } }`);
const main = menus.menus.nodes.find((menu) => menu.handle === 'main-menu');
if (!main) throw new Error('The store has no main-menu.');

const answer = await graphql(
  `mutation ($id: ID!, $title: String!, $items: [MenuItemUpdateInput!]!) {
    menuUpdate(id: $id, title: $title, items: $items) {
      menu { items { id title items { id title items { id title } } } }
      userErrors { field message }
    }
  }`,
  { id: main.id, title: main.title, items },
);
if (answer.menuUpdate.userErrors.length) throw new Error(JSON.stringify(answer.menuUpdate.userErrors));

// The English titles: each item's own translation, keyed by its Spanish name.
const english = new Map([['Inicio', 'Home'], ...categories.map((category) => [category.name.es, category.name.en])]);
const written = [];
const walk = (nodes) => nodes.forEach((node) => { written.push(node); walk(node.items ?? []); });
walk(answer.menuUpdate.menu.items);

let translated = 0;
for (const item of written) {
  // A menu item is a MenuItem to the menus and a Link to translations.
  const resource = item.id.replace('/MenuItem/', '/Link/');
  const content = await graphql(`query ($id: ID!) { translatableResource(resourceId: $id) { translatableContent { key digest } } }`, { id: resource });
  const digest = content.translatableResource?.translatableContent.find((entry) => entry.key === 'title')?.digest;
  if (!digest || !english.has(item.title)) continue;
  const registered = await graphql(
    `mutation ($id: ID!, $translations: [TranslationInput!]!) { translationsRegister(resourceId: $id, translations: $translations) { userErrors { message } } }`,
    { id: resource, translations: [{ locale: 'en', key: 'title', value: english.get(item.title), translatableContentDigest: digest }] },
  );
  if (registered.translationsRegister.userErrors.length) throw new Error(JSON.stringify(registered.translationsRegister.userErrors));
  translated++;
}

const show = (nodes, depth = 0) => nodes.forEach((node) => { console.log(`${'  '.repeat(depth)}${node.title}`); show(node.items ?? [], depth + 1); });
show(answer.menuUpdate.menu.items);
console.log(`\n${written.length} items in the main menu, ${translated} with their English title.`);
