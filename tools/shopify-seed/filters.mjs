// Declares the product metafields the store's filters can offer (phase 16, the
// store in front). The seeding script writes every seed attribute as a
// `tendero.*` metafield; Shopify's storefront filters (the free Search &
// Discovery app) can only offer one that has a *definition*, so this script
// writes the definitions, named in the store's language. Choosing them as
// filters is done in that app, which has no public API.
// Run from the repository root:
//   node tools/shopify-seed/filters.mjs
import { connect } from './shopify.mjs';

const NAMESPACE = 'tendero';

// The attributes worth a filter: present on many products, with few values.
const filters = [
  { key: 'color', name: 'Color', description: 'El color del producto' },
  { key: 'material', name: 'Material', description: 'El material principal' },
  { key: 'genero', name: 'Género', description: 'Para quién es la prenda' },
  { key: 'induccion', name: 'Apto para inducción', description: 'Si sirve en placas de inducción' },
];

const { graphql } = await connect();

for (const filter of filters) {
  const existing = await graphql(
    `query ($namespace: String!, $key: String!) {
      metafieldDefinitions(first: 1, ownerType: PRODUCT, namespace: $namespace, key: $key) { nodes { id } }
    }`,
    { namespace: NAMESPACE, key: filter.key },
  );
  if (existing.metafieldDefinitions.nodes.length) {
    console.log(`${NAMESPACE}.${filter.key}  already defined`);
    continue;
  }

  const answer = await graphql(
    `mutation ($definition: MetafieldDefinitionInput!) {
      metafieldDefinitionCreate(definition: $definition) { createdDefinition { id } userErrors { field message } }
    }`,
    {
      definition: {
        ownerType: 'PRODUCT',
        namespace: NAMESPACE,
        key: filter.key,
        type: 'single_line_text_field',
        name: filter.name,
        description: filter.description,
        access: { storefront: 'PUBLIC_READ' },
      },
    },
  );
  if (answer.metafieldDefinitionCreate.userErrors.length)
    throw new Error(`${filter.key} ${JSON.stringify(answer.metafieldDefinitionCreate.userErrors)}`);
  console.log(`${NAMESPACE}.${filter.key}  defined as «${filter.name}»`);
}
