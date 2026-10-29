// The seed's warehouses and stock in the development store (phase 16, S2a,
// P16-15), so the stock import has something real to read back. Run from the
// repository root, after seed.mjs:
//   node tools/shopify-seed/stock.mjs
//
// The store's locations become the seed's two warehouses: the store's own
// first location is renamed for Madrid (a new store has one, and renaming it
// keeps it the default), and Barcelona is added. Which location is which
// warehouse is written to locations.json beside this script — the mapping a
// person writes for a merchant's store, written here by the script because
// the script made the locations. Then each variant's units on hand in each
// location are set from seed/stock.sample.json: a SKU the seed does not stock
// somewhere has nothing there. A run can be repeated; it writes only what
// differs. The app needs write_inventory and write_locations.
import { randomUUID } from 'node:crypto';
import fs from 'node:fs';
import { connect } from './shopify.mjs';

const warehouses = JSON.parse(fs.readFileSync('seed/warehouses.sample.json', 'utf8'));
const stock = JSON.parse(fs.readFileSync('seed/stock.sample.json', 'utf8'));
const { graphql } = await connect();

/** Where a location the script adds stands: Shopify needs an address, and the seed's warehouses have none. */
const ADDRESSES = {
  MAD: { address1: 'Calle de Alcalá 1', city: 'Madrid', zip: '28014', countryCode: 'ES', provinceCode: 'M' },
  BCN: { address1: 'Carrer de Mallorca 1', city: 'Barcelona', zip: '08013', countryCode: 'ES', provinceCode: 'B' },
};

// ---- Locations: one per warehouse, by its Spanish name (the store's language).
const listed = await graphql(`
  {
    locations(first: 50, includeInactive: true) {
      nodes {
        id
        name
        isActive
      }
    }
  }
`);
const locations = listed.locations.nodes;
const mapping = {};

for (const [index, warehouse] of warehouses.entries()) {
  let location = locations.find((each) => each.name === warehouse.name.es);

  if (!location && index === 0) {
    // The store's first location, renamed: a store always has one, and it is the default.
    const first = locations.find((each) => each.isActive && !warehouses.some((other) => other.name.es === each.name));
    const answer = await graphql(
      `
        mutation ($id: ID!, $input: LocationEditInput!) {
          locationEdit(id: $id, input: $input) {
            location {
              id
              name
              isActive
            }
            userErrors {
              message
            }
          }
        }
      `,
      // With its address: a new store's first location has none, and Shopify edits none without one.
      { id: first.id, input: { name: warehouse.name.es, address: ADDRESSES[warehouse.code] } },
    );
    refuseOn(answer.locationEdit.userErrors, warehouse.code);
    location = answer.locationEdit.location;
    console.log(`${warehouse.code}  renamed "${first.name}" → "${location.name}"`);
  }

  if (!location) {
    const answer = await graphql(
      `
        mutation ($input: LocationAddInput!) {
          locationAdd(input: $input) {
            location {
              id
              name
              isActive
            }
            userErrors {
              message
            }
          }
        }
      `,
      { input: { name: warehouse.name.es, fulfillsOnlineOrders: true, address: ADDRESSES[warehouse.code] } },
    );
    refuseOn(answer.locationAdd.userErrors, warehouse.code);
    location = answer.locationAdd.location;
    console.log(`${warehouse.code}  added "${location.name}"`);
  }

  mapping[location.id] = warehouse.code;
}

fs.writeFileSync('tools/shopify-seed/locations.json', `${JSON.stringify(mapping, null, 2)}\n`);
const locationOf = Object.fromEntries(Object.entries(mapping).map(([id, code]) => [code, id]));

// ---- Stock: each variant's units on hand in each warehouse, as the seed says.
const wanted = new Map(stock.map((level) => [`${level.sku}|${level.warehouse}`, level.onHand]));
const changes = [];
let activated = 0;
let tracked = 0;

for await (const variant of variants()) {
  const item = variant.inventoryItem;
  if (!item.tracked) {
    const answer = await graphql(
      `
        mutation ($id: ID!, $input: InventoryItemInput!) {
          inventoryItemUpdate(id: $id, input: $input) {
            userErrors {
              message
            }
          }
        }
      `,
      { id: item.id, input: { tracked: true } },
    );
    refuseOn(answer.inventoryItemUpdate.userErrors, variant.sku);
    tracked++;
  }

  for (const warehouse of warehouses) {
    const onHand = wanted.get(`${variant.sku}|${warehouse.code}`) ?? 0;
    const level = item.inventoryLevels.nodes.find((each) => each.location.id === locationOf[warehouse.code]);
    const current = level?.quantities.find((quantity) => quantity.name === 'on_hand')?.quantity;

    if (!level && onHand > 0) {
      const answer = await graphql(
        `
          mutation ($item: ID!, $location: ID!, $onHand: Int, $key: String!) {
            inventoryActivate(inventoryItemId: $item, locationId: $location, onHand: $onHand) @idempotent(key: $key) {
              userErrors {
                message
              }
            }
          }
        `,
        { item: item.id, location: locationOf[warehouse.code], onHand, key: randomUUID() },
      );
      refuseOn(answer.inventoryActivate.userErrors, `${variant.sku} ${warehouse.code}`);
      activated++;
    } else if (level && current !== onHand) {
      // No compare-and-swap: the seed is the truth, whatever the store holds.
      changes.push({
        inventoryItemId: item.id,
        locationId: locationOf[warehouse.code],
        quantity: onHand,
        changeFromQuantity: null,
      });
    }
  }
}

for (let start = 0; start < changes.length; start += 250) {
  const answer = await graphql(
    `
      mutation ($input: InventorySetQuantitiesInput!, $key: String!) {
        inventorySetQuantities(input: $input) @idempotent(key: $key) {
          userErrors {
            code
            message
          }
        }
      }
    `,
    {
      input: {
        name: 'on_hand',
        reason: 'correction',
        referenceDocumentUri: 'tendero://seed/stock.sample.json',
        quantities: changes.slice(start, start + 250),
      },
      key: randomUUID(),
    },
  );
  refuseOn(answer.inventorySetQuantities.userErrors, 'quantities');
}

console.log(
  `\n${tracked} items tracked, ${activated} levels added, ${changes.length} levels set; mapping in tools/shopify-seed/locations.json.`,
);

/** Every variant with its inventory item and levels, a page at a time. */
async function* variants() {
  let after = null;
  do {
    const page = await graphql(
      `
        query ($after: String) {
          productVariants(first: 100, after: $after) {
            pageInfo {
              hasNextPage
              endCursor
            }
            nodes {
              sku
              inventoryItem {
                id
                tracked
                inventoryLevels(first: 10) {
                  nodes {
                    location {
                      id
                    }
                    quantities(names: ["on_hand"]) {
                      name
                      quantity
                    }
                  }
                }
              }
            }
          }
        }
      `,
      { after },
    );
    yield* page.productVariants.nodes;
    after = page.productVariants.pageInfo.hasNextPage ? page.productVariants.pageInfo.endCursor : null;
  } while (after);
}

function refuseOn(errors, about) {
  if (errors?.length) throw new Error(`${about} ${JSON.stringify(errors)}`);
}
