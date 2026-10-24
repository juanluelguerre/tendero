# Spec: exports and feeds — leaving Tendero, and selling elsewhere

**Status**: accepted 2026-10-24, its five decisions as proposed.
**Slice(s)**: a new `Exports` project beside the contexts (an outbound port and
its format adapters), and one slice per export.
**Article**: *An import without an export is lock-in.*

## Problem

Phase 16 helps a merchant leave Shopify. The same argument turned around: a
merchant must be able to **leave Tendero**, or the platform is the lock-in it
helped them escape. A shop also needs its data **outside itself** every week, not
only on the day it leaves:
- a product feed for Google Merchant Center and Meta, which is where shops sell;
- the catalogue and its stock in a form another platform imports (Shopify's
  product CSV, the first one a merchant asks for);
- orders for the accountant, as a spreadsheet;
- and, already built, a shopper's own data (`YourData`, phase 15, GDPR art. 20).

Today nothing leaves Tendero except that last one and the API itself.

## What exists today, measured 2026-10-24

- **Read ports across the contexts**: `IProductCatalogReader` (Catalog),
  `IPricedItemReader` and `IPriceResolver` (Pricing), `IAvailabilityReader`
  (Inventory). The search projection already composes all three; a feed needs
  the same facts.
- **The inbound side has the shape to mirror**: `ICatalogSourceConnector` and
  `IStockSourceConnector`, one adapter per source, keyed, with a contract suite.
- **`YourData`** exports one person's data across contexts through
  `IPersonalDataHolder`. It is the only export, and it is per person.
- **No spreadsheet library** is a dependency. A CSV needs none.

## Behaviour, proposed

**An outbound port, mirrored on the inbound ones.** Exports live in a new project,
`ElGuerre.Tendero.Exports`, which is not a context: it owns no state, reads only
through the contexts' read ports, and never reaches a domain (the rule
`An_adapter_never_reaches_a_domain` already holds this for Shopify). Each **what**
is a slice (catalogue feed, catalogue for another platform, orders). Each **how**
is a format adapter, keyed (`csv`, `tsv`, `xlsx`, `google-merchant`,
`shopify-csv`), so a new format is a class and a registration line, with no `if`s
in the slice. Each adapter passes a contract suite: every row the slice gives
comes out, in order, with its money in the currency's decimals, and the file reads
back with the format's own reader.

**The exports, in the order of their value:**

1. **The product feed** (Google Merchant Center's product data specification,
   checked on the day the adapter is written). One row per **variant**, grouped by
   `item_group_id` = the product's code: `id` (the SKU), `title` and `description`
   in the feed's language, `link` to the storefront's `/<culture>/p/<slug>/<code>`,
   `image_link`, `price` with its currency, `availability` from the ledger
   (`in_stock` / `out_of_stock`), `brand`, `gtin` (the barcode, when there is one),
   the colour, size and gender from the variant's attributes, and
   `google_product_category` from a mapping a person writes, as Shopify's
   categories are. Only products on sale. One feed per culture.
2. **The catalogue for another platform**: Shopify's product CSV (handle, title,
   description as HTML, vendor, type, options and their values, SKU, barcode,
   price, inventory, image and its alt text, status). It closes phase 16's round
   trip from the other side (Tendero → Shopify), and it is the proof that a
   merchant can leave.
3. **Orders for the accountant**: one row per order line, with the order's number,
   date in the shop's day (ADR 0046), customer, line, quantity, unit price,
   discount, tax class and rate, tax and total. Money as numbers, not text, so the
   spreadsheet sums it.

**CSV is written for the spreadsheet a person opens it in**: UTF-8 with a BOM
(without it, Excel shows *Ã¡* for *á*), and the separator a decision below
settles. A feed follows its consumer's own rules instead (Merchant Center reads
tab-separated UTF-8).

**Who may export what.** The feed and the catalogue export carry no personal data:
Shopkeeper, audited as any command. Orders carry personal data (names,
addresses): Shopkeeper only, one audit row per export with what was exported and
by whom, and a test that a customer's erasure leaves the export with nothing of
them that the six years' blocking does not keep.

**Size.** At laboratory scale (120 products, a few hundred variants) an export is
an HTTP download built in the request. Generating it in the background through the
outbox, for a catalogue where that matters, is deferred with its measured time
recorded (the scale rule).

**The degraded path.** An export reads only Postgres and the committed files: no
model, no index, no Qdrant. A missing image or barcode leaves the field empty and
the row in; a product without a category mapping goes in without
`google_product_category`, and the answer counts how many.

## Non-goals

- **Importing these formats back.** The inbound side is phase 16's ports.
- **Pushing to Merchant Center's API** (Content API / Merchant API). The feed is a
  file or a URL Merchant Center fetches; an API client is a later step if a merchant
  needs it.
- **A report designer.** The exports are fixed shapes. The backoffice copilot
  (Steward) already answers ad-hoc questions.
- **Customers as a spreadsheet.** A list of people with their emails is the export
  most likely to leak, and no request so far needs it; `YourData` covers the
  person's own.

## Measures

- A contract suite per format adapter, run on every one.
- The product feed validated against Google's specification by a test over the
  seed: every required field present for every row, and the variants grouped.
- The round trip from the other side: the seed exported as Shopify's CSV and read
  back by Shopify's importer on the lab store, compared field by field (the same
  comparison phase 16's round trip makes).
- The orders export summed in a test and compared with the orders' totals.

## Evidence of use (ADR 0036)

`tendero.exports.generated`, a counter by `export.kind` (feed, catalogue, orders)
and `export.format`, with the rows written.
- **The question:** which exports are used, and in which format?
- **The decision it changes:** whether XLSX earns its dependency, and whether a
  Merchant API client is worth building over a fetched file.

## Decisions for the author

1. **Where exports live: a new `Exports` project**, not a context (it owns no
   state), reading through the contexts' read ports. *Proposed: yes*, as `Shopify`
   is an adapter project and not a context.
2. **The first slice is the product feed**, then the Shopify CSV, then orders.
   *Proposed: yes*: the feed is what a working shop uses every week; the Shopify
   CSV is what makes leaving credible; orders are what an accountant asks for.
3. **XLSX waits for its evidence.** A CSV for the spreadsheet (UTF-8 with BOM)
   covers the accountant. XLSX needs a library (ClosedXML is MIT; the exact
   version's licence is checked before adding it, Tier 1) and is added when the
   counter says people export orders and ask for more than CSV. *Proposed: CSV
   first, no new dependency.*
4. **The CSV's separator: `;` for es, `,` for en**, following the reader's
   culture as every other answer does (`?culture=` → `Accept-Language`), because a
   Spanish Excel splits on `;` and an English one on `,`. *Proposed: yes.*
5. **The feed is a public URL** (`/feeds/google/<culture>.tsv`, `AllowAnonymous`,
   only products on sale), for Merchant Center to fetch on its schedule, as a
   storefront page is public. *Proposed: yes*, with a download in the backoffice
   beside it.

## Tasks, once accepted

- `E-1` The `Exports` project, the outbound port and its contract suite, the
  architecture rule extended to it — **S**
- `E-2` The product feed: the slice, the Google Merchant adapter, the category
  mapping file, the public URL and the backoffice download, the counter — **M**
- `E-3` The Shopify CSV adapter, and the round trip from the other side on the lab
  store — **M**
- `E-4` Orders for the accountant: the slice, the CSV adapter for the reader's
  culture, the audit and the erasure test — **M**
