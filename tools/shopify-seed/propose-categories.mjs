// Proposes Shopify's Standard Product Taxonomy category for each category the
// seed's products use, searching the taxonomy by the category's English name
// (phase 16, P16-6). It only PROPOSES: categories.proposed.json lists each
// code with the candidates found, and a person writes categories.json from it
// (spec decision 5). Run from the repository root:
//   node tools/shopify-seed/propose-categories.mjs
import fs from 'node:fs';
import { connect } from './shopify.mjs';

const products = JSON.parse(fs.readFileSync('seed/products.sample.json', 'utf8'));
const tree = JSON.parse(fs.readFileSync('seed/categories.sample.json', 'utf8'));
const flat = [];
(function walk(nodes) {
  for (const node of nodes) {
    flat.push(node);
    if (node.children) walk(node.children);
  }
})(Array.isArray(tree) ? tree : (tree.categories ?? [tree]));

const used = [...new Set(products.map((product) => product.product_type))];
const { graphql } = await connect();

const query = `query ($search: String!) {
  taxonomy { categories(search: $search, first: 5) { nodes { id fullName } } }
}`;

const proposed = {};
for (const code of used) {
  const name = flat.find((node) => node.code === code)?.name?.en ?? code;
  const data = await graphql(query, { search: name });
  proposed[code] = { name, candidates: data.taxonomy.categories.nodes };
  console.log(`${code} (${name}): ${data.taxonomy.categories.nodes[0]?.fullName ?? 'nothing found'}`);
}

fs.writeFileSync('tools/shopify-seed/categories.proposed.json', JSON.stringify(proposed, null, 2) + '\n');
