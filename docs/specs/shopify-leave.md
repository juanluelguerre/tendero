# Spec: leaving Shopify (phase 16, stage S2)

**Status**: accepted 2026-10-23, all nine decisions as proposed. S2a comes
first. One correction came from reading the code before writing S2a's tasks:
**warehouses are a file nothing writes**, as promotions are. So a store's
locations become a *proposed warehouses file* that a person commits, while
stock enters through the ledger's stocktake (`IStockLedger.AdjustAsync`),
which is what a migration is.
**Branch**: `feature/shopify`, the same long-lived branch as S1.
**Context**:
- [`docs/analysis/tendero-suite-shopify.md`](../analysis/tendero-suite-shopify.md)
  §10, case A, where a merchant imports everything once, checks it, switches
  the domain and closes the store;
- S1's spec, [`shopify-import.md`](shopify-import.md), closed 2026-10-23;
- the board's phase 16, which adds each variant's tax class.

**Article**: *Leaving Shopify without losing your search ranking.*

## Problem

S1 brings a store's catalogue. A merchant who leaves needs everything that
makes the shop *theirs*, and a way to switch without losing what search
engines and customers already know:
- the stock in each place;
- the people who bought, and what they bought;
- the discounts they were promised;
- the addresses search engines have indexed.

Each of these belongs to a different context. Each one meets a rule of ours
that was written before anyone migrated.

## What exists today, measured 2026-10-23

- **Catalogue (S1):**
  - products, variants with the source's id, SKU, barcode and price,
    pictures, es/en texts, the category through a mapping, and attributes from
    `tendero.*` metafields;
  - the product's **handle is kept** (`ExternalReference.Handle`);
  - importing never publishes (ADR 0012).
- **Tax:** a class per variant (`Variant.TaxClass`), Spanish rates, VAT
  extracted from the price (ADR 0028). The seed sets none, and S1 does not bring
  it.
- **Stock:** `Inventory` keys stock by SKU over warehouses. The lab's stock and
  warehouses come from seed files (`seed/stock.sample.json`,
  `warehouses.sample.json`). The ledger (`IStockLedger`) reserves, commits and
  releases for orders. There is no inbound port for stock from a source.
- **Customers:** `Accounts` keys its aggregates (the address book, preferences,
  consent) by the customer's id from the identity provider. Identity is a port,
  and **we only ever validate tokens** (invariant 10): nothing creates users in
  Keycloak.
- **Orders:** `Ordering`'s state machine (`Order.AllowedTransitions`, invariant
  5) moves an order only through `TransitionTo`. Lines snapshot the product
  (invariant 1).
- **Promotions:** `Pricing` reads committed files and owns no table. Nothing
  writes a promotion.
- **Gift cards:** Tendero has none.
- **The storefront** is a single-page app with no server rendering. A real 301
  needs a server to answer the old address.

## Behaviour, proposed

### Connecting to a merchant's own store

S1's client-credentials grant works only inside our organisation. A merchant's
store needs the **authorization-code grant**:
1. the app is distributed to that one store;
2. the merchant installs it;
3. Shopify hands back an **offline token** that does not expire.

Proposed: a small command-line step (`tools/shopify-connect/`) runs the install
once and tells the operator to save the token as a secret (`Shopify:AccessToken`).
The adapter then accepts either credential. **No token is stored in the
database**, and the API gains no OAuth callback.

### The dry run, then the migration

One operation, run twice:
- **as a rehearsal**, which reads everything and writes nothing. It reports, per
  context, what will come and what will not (each with an `ImportNoteCode`, as
  S1 does). The merchant decides before the day.
- **for real.**

Each context is brought by **its own inbound port**, beside the catalogue's.
Each port has a Shopify adapter and a contract suite. No context reaches into
another's tables.

### What comes, context by context

| what | to | how | notes |
|---|---|---|---|
| **Tax class per variant** | `Catalog` | the variant's `taxable` flag and the store's overrides per collection and country, mapped to `standard`, `reduced`, `super-reduced`, `exempt` | an override by destination is noted: Tendero has no rates by destination (backlog) |
| **Locations → warehouses**, **stock per location** | `Inventory` | a new inbound port, `IStockSourceConnector`; quantities enter the ledger as a **receipt**, never as an edit | needs `read_inventory`, `read_locations` |
| **Customers** | the identity provider + `Accounts` | a new port, **provisioning**: create the person in the issuer (a Keycloak adapter, and the dev issuer's own) with no password and a required action to set one; then their addresses into the address book and their marketing consent with its date | protected customer data: Shopify asks the app to request access. Passwords never come (no platform exports them) |
| **Order history** | `Ordering` | orders **born closed** (delivered, cancelled or refunded), lines snapshotted, totals and taxes as they were, no stock moved and no event that sends anything | an ADR: invariant 5 gains a named, narrow exception (decision 4) |
| **Discounts** | `Pricing` | translated into a **proposed promotions file**, which a person reviews and commits; what the engine cannot express is noted | Pricing has no writer, so the proposal *is* the migration (decision 5) |
| **Gift cards** | — | **listed with their balances** in the report, for the merchant to reissue | Tendero has none; Shopify returns a full code only when it creates the card (decision 6) |
| **Redirects** | the storefront's host | a 301 from `/products/<handle>`, `/collections/<handle>` and `/pages/…` to Tendero's routes. Products from S1's handles; collections from the category mapping | where a 301 is answered is decision 7 |
| **Publishing what came** | `Catalog` | a person publishes the migrated products **in bulk**, audited; the import never does | ADR 0012 amended (decision 8) |

### What the lab store needs

The development store has the seed's catalogue (S1). To have something to
migrate, the seeding script grows:
- **stock** in two locations;
- **a few customers** with addresses;
- **past orders**, through `orderCreate` on a development store;
- **two discounts**: one the engine can express and one it cannot.

That means more scopes, read and write:
- customers;
- orders, plus `read_all_orders` for anything older than sixty days;
- inventory and locations;
- discounts;
- online store navigation, for the store's own redirects.

The author adds them in a new version, as in S1.

### The degraded path

- **Without a merchant's token**, the connection is the development store's,
  as in S1.
- **A context whose port has no adapter** is reported as not brought, and the
  rest migrate.
- **The rehearsal** writes nothing, by construction: it calls the readers and
  the notes, and never the writers.

## Testing

- **A contract suite per new port** (stock, customers, orders), each with a
  fake beside the Shopify adapter, on recorded fixtures. No network in CI.
- **The round trip, grown:** the lab store's stock, customers, orders and
  discounts read back. It compares quantities per SKU and location, customers
  and their addresses, each order's lines and totals, and the proposed
  promotions against the seeded discounts.
- **Redirects:** each old address answers 301 to the right new one, held by a
  test over the handles the store has.

## Non-goals

- Reviews, blog posts and pages' content, themes and apps' data.
- Changing the domain's DNS: that is the merchant's step.
- Selling through Shopify, which is S3.

## Measures

- The rehearsal against the migration: everything the rehearsal said would come
  did, and nothing else.
- The round trip per context, as above.
- Redirects: the share of the store's known addresses that answer 301 to a live
  page (the target is all of them).

## Evidence of use (ADR 0036)

The import's metrics (S1) gain a `catalog.source` per context and the notes'
codes for the new contexts.
- **The question:** how much of a store comes across, per context?
- **The decision it changes:** which context's port or mapping to improve
  first.

## Decisions for the author

Each has a proposal.

1. **Split S2 in two**: *S2a*, the shop's goods (tax class, stock and
   warehouses, redirects, bulk publication, the rehearsal, a merchant's own
   token); then *S2b*, the people and their history (customers, orders,
   discounts, gift cards). *Proposed: yes.* S2a is already a shop a merchant
   could open. S2b is where the privacy and identity questions are, and it
   deserves its own review.
2. **A merchant's store connects by a one-off command-line install**, and the
   offline token is a secret; no OAuth callback in the API and no token in the
   database. *Proposed: yes.*
3. **Customers are provisioned through a new port**, with an adapter for
   Keycloak and one for the dev issuer, and no password. *Proposed: yes.* It
   creates identities, it never authenticates them, so invariant 10 holds. An
   ADR records why provisioning is not "writing an authorization server".
4. **Imported orders are born closed**, through one named factory
   (`Order.Imported(...)`) that the state machine's table lists as an entry
   point, with no transition and no event that sends anything. *Proposed: yes.*
   An ADR, because it is the first exception to invariant 5.
5. **Discounts become a proposed promotions file** that a person commits, not a
   table: Pricing still has no writer. *Proposed: yes.*
6. **Gift cards are listed, not migrated**, with their balances, for the
   merchant to reissue. *Proposed: yes.* Tendero would need gift cards as a
   payment method first, which is a feature of its own.
7. **Redirects are answered by the API** at the old paths, for the storefront's
   host to forward. Each answer is a 301 to the storefront's route, from a table
   the migration fills (handle → product code, collection handle → category).
   *Proposed: yes.* It works for any host that can forward unknown paths, and is
   testable without one. The alternative, server rendering for the storefront,
   is much larger.
8. **Bulk publication of a migration**: a person publishes *what one migration
   brought*, in one audited operation; the import still never publishes.
   *Proposed: yes*, as an amendment to ADR 0012.
9. **The lab store's data for S2 is seeded by the same script, grown**, and the
   author adds the scopes in a new version. *Proposed: yes.*

## Tasks (S2a, the shop's goods)

Written 2026-10-23, with the store's scopes for it already granted (inventory,
locations, publications, navigation).

- `P16-8` **Where Shopify's adapters live** (a decision for the author). S1's
  client and adapter sit in `Catalog/Connectors/Shopify`. Stock needs an
  adapter of an `Inventory` port. `Inventory` may reference the SharedKernel
  alone (an architecture rule), so that adapter cannot live there, and putting
  it in `Catalog` would make one context implement another's port. *Proposed:*
  a project of its own, `src/Shopify` (`ElGuerre.Tendero.Shopify`), holding the
  client and every Shopify adapter (catalogue source, stock source, later
  orders and customers), referencing the ports it implements. It follows
  invariant 3: an external system reached through adapters, one place to read
  Shopify. S1's code moves there unchanged, with its tests — **S**
- `P16-9` **Tax class per variant**: `ExternalVariant` gains `TaxClass`, the
  adapter maps Shopify's `taxable` (true → `standard`, false → `exempt`), and
  the mapper sets the variant's class. A note (`TaxOverrideAtSource`) when the
  store's tax overrides cannot be expressed — **S** · *Done 2026-10-23*: no note,
  because the store's overrides by collection or country are not in the Admin
  API's product, and Shopify has no flag for a reduced rate: `taxable` is all
  there is. A source that says nothing leaves the variant's class as it is, so
  the seed is unchanged; the round trip compares classes, none read as standard
- `P16-10` **Stock and warehouses**: an inbound port in `Inventory`
  (`IStockSourceConnector`: locations, then levels per SKU and location), with
  its contract suite and a fake; the Shopify adapter (bulk read of inventory
  levels); an `ImportStock` slice that enters each level through the ledger's
  stocktake (`AdjustAsync`) and writes the store's locations as a **proposed
  warehouses file** for a person to commit — **M**. *As built (2026-10-23)*:
  each level is a **count** (`IStockLedger.CountAsync`), not an adjustment — a
  migration is a stocktake, and only a count can say zero; the proposed
  warehouses come in the answer (`ProposedWarehouses`) rather than as a file
  written beside it, for a person to add to `warehouses.json`. A location is a
  warehouse through `Shopify:LocationsFile`, written by a person as the
  categories are; a variant is the SKU the catalogue sells it under, asked of
  the catalogue through a new port (`ISourceVariantSkus`), since the store's SKU
  is ours only when no sibling shared it. An unmapped location, a variant never
  imported and a level in a warehouse the shop does not operate are notes, and
  their stock is not entered.
- `P16-11` **Redirects**: the API answers `/products/<handle>` and
  `/collections/<handle>` with a 301 to the storefront's `/p/<slug>/<code>` and
  `/c/<code>`, from the handles S1 keeps and the store's collections mapped to a
  category (one whose products share it; otherwise a note). The storefront's
  development server forwards those paths to the API; a production host does
  the same — **M**. *As built (2026-10-23)*: the source's port gained
  `StreamCollectionsAsync` (empty by default), and Shopify reads every
  collection with its products in a bulk operation of its own. The import keeps
  each collection as the **deepest category all its saved products are in**
  (`CategoryTree.SharedBy`) — a department's collection becomes the department,
  or the subcategory when all its products are in one — in
  `catalog.source_collections`; a collection with no category in common, or
  none of whose products came in, is a `CollectionNotMapped` note. The API
  answers `/products/<handle>`, `/collections/<c>/products/<handle>` and
  `/collections/<handle>`, each also under `/es/` or `/en/` as a store with two
  languages writes them, with a 301 to `/<culture>/p/<slug>/<code>` or
  `/<culture>/c/<code>` — the culture from the path, else the browser's. **A
  product redirects only when it is on sale**: before publication, or after a
  withdrawal, the answer is 404, because a permanent redirect to a page that
  answers 404 is cached by every browser and crawler. The endpoints are outside
  the OpenAPI document (addresses for browsers, not a client's API).
  Measured against the store: all 30 seeded collections lead to a page, 27 to
  their own category and 3 to a subcategory holding all their products. Not
  covered: a store's *translated* handles (`/en/products/<english-handle>`),
  which the import does not read; the base handle under `/en/` works.
  `tendero.catalog.source_addresses` counts what is asked and whether it led
  somewhere.
- `P16-12` **Bulk publication**: a person publishes what one migration
  brought, in one audited operation (ADR 0012 amended) — **S**. *As built
  (2026-10-23)*: the import records a run (`ImportRun`) with the products it
  created and answers with its id; `POST /api/catalog/imports/{id}/publish`
  publishes the ones still in Draft, in one save and one audit row, and counts
  the rest. A product that was on sale and was withdrawn is left in Draft —
  `Product.FirstPublishedAt` tells it from one never reviewed (ADR 0012,
  amendment).
- `P16-13` **The rehearsal**: the import and the stock import run with nothing
  written, reporting what would come and the notes — **S**
- `P16-14` **A merchant's own store**: `Shopify:AccessToken` accepted beside
  the client credentials, and `tools/shopify-connect/` running the
  authorization-code install once — **M**
- `P16-15` The seeding script grows (two locations, MAD and BCN, and the seed's
  stock), the round trip grows (stock per SKU and location, tax classes,
  redirects), the notebook, S2a's close — **M**

S2b's tasks are written when S2a closes.
