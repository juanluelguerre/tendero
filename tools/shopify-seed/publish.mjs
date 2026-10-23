// Publishes every product of the development store to its Online Store sales
// channel, so the store's own storefront (case B, phase 16) shows the seeded
// catalogue instead of the theme's placeholders. Needs `write_publications`.
// Run from the repository root:
//   node tools/shopify-seed/publish.mjs
//
// Publishing is also what gives a product its `publishedAt`, the date the
// importer reads as "available from": Shopify sets it, now, never in the past.
import { connect } from './shopify.mjs';

const { graphql } = await connect();

const channels = await graphql(`{ publications(first: 20) { nodes { id name } } }`);
const store = channels.publications.nodes.find((publication) => publication.name === 'Online Store');
if (!store) throw new Error(`No Online Store channel among: ${channels.publications.nodes.map((p) => p.name).join(', ')}`);

let published = 0;
let cursor = null;
do {
  const page = await graphql(
    `query ($after: String) { products(first: 50, after: $after) { nodes { id } pageInfo { hasNextPage endCursor } } }`,
    { after: cursor },
  );
  for (const { id } of page.products.nodes) {
    const answer = await graphql(
      `mutation ($id: ID!, $input: [PublicationInput!]!) {
        publishablePublish(id: $id, input: $input) { userErrors { field message } }
      }`,
      { id, input: [{ publicationId: store.id }] },
    );
    if (answer.publishablePublish.userErrors.length)
      throw new Error(`${id} ${JSON.stringify(answer.publishablePublish.userErrors)}`);
    published++;
  }
  cursor = page.products.pageInfo.hasNextPage ? page.products.pageInfo.endCursor : null;
} while (cursor);

console.log(`${published} products published to ${store.name}.`);
