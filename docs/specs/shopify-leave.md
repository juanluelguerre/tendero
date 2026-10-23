# Spec: leaving Shopify (phase 16, stage S2)

**Status**: proposed 2026-10-23, waiting on the author's *Decisions* at the
end. Nothing is built until they are taken.
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

## Tasks

Written when the decisions are taken, one list per half if decision 1 holds.
