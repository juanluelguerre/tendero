# Spec: importing a Shopify store's catalogue (phase 16, stage S1)

**Status**: accepted 2026-10-23. The author took all nine decisions as proposed,
after asking whether Shopify's descriptions really are HTML (they are:
`descriptionHtml`, while `description` is the same text stripped to a single
line). Two corrections came from reading the code before `P16-2`, and are
marked *corrected* where they apply: a variant's price already lives in the
catalogue, and a SKU is never renamed.
**Branch**: `feature/shopify`, long-lived. All of phase 16 (S1 to S3) is built on
it, because it will take longer to line up with `develop` than one phase usually
does (the author, 2026-10-23).
**Context**: [`docs/analysis/tendero-suite-shopify.md`](../analysis/tendero-suite-shopify.md)
§9 to §16. Its three cases are a merchant leaving Shopify (A), Shopify as the
storefront over Tendero's engine (B) and a suite on Shopify (C). Its phases run
S1 to S5. The author placed S1, S2 and S3 next, and postponed S4 and S5.
**Slice(s)**: `Catalog` (a Shopify adapter of `ICatalogSourceConnector`; the
contract's variant grows; `VariantExternalReferences`, waiting since `P1-2`),
the AppHost (the store's credentials as secret parameters), and a script that
fills the development store from the seed.
**Article**: *A second real source: importing a Shopify store through the same port.*

## Problem

The inbound port has had one adapter since phase 0: the seed, a file we wrote.
Article 02 promised that adding Shopify is "one class and one registration line",
and that five inherited contract tests would say whether it behaves. That promise
has never met a real source.

A real source is different from the seed in three ways:
- **Its variants have identities of their own**: an id, a SKU, a barcode and a
  price per variant.
- **Its texts are HTML**, in a primary language plus translations.
- **Its categories are someone else's taxonomy.**

S1 is that meeting. It is also the base that S2 (leaving Shopify) and S3
(Shopify as a channel) reuse: one client and one mapping, built once.

## What exists today, measured 2026-10-23

- **The port.** `ICatalogSourceConnector`
  (`src/Catalog/Connectors/ICatalogSourceConnector.cs`): `Source` plus
  `StreamProductsAsync`, an `IAsyncEnumerable<ExternalProduct>`. It is
  registered as a keyed service under its source name. `ImportProducts` knows
  only the interface. Its validator refuses a source that is not registered
  and names the ones that are.
- **`ExternalProduct`** has names and descriptions per culture, a brand, a
  category code, **one price**, images (each with `Shows`, the variant it
  pictures), attributes, `AvailableFrom` and `Variants`.
- **An `ExternalVariant` is only `Key` and `Attributes`.** Our SKU is
  `"{ExternalId}-{Key}"` (`ExternalProductMapper.SkuOf`). A variant has no id,
  SKU, barcode or price of its own from the source.
- **`VariantExternalReferences` is not built.** `P1-2` deferred it to "the
  connector that does carry per-variant source ids", which is this one.
- **Prices and stock are not the catalogue's.** `Pricing` reads committed price
  lists (`seed/pricelists.sample.json`, `PricingSeedFiles`), and `Inventory`
  reads `seed/stock.sample.json`. The product's own price is the catalogue's
  base price.
- **The category is a code** from the seed's taxonomy
  (`seed/categories.sample.json`). Nothing in the domain refuses an unknown
  one, and `CatalogueGaps` reports what a category requires and a product lacks.
- **Importing never publishes** (invariant 5, ADR 0012). A re-import updates
  what the source owns and never touches status.
- **Images are ingested, never linked** (ADR 0011). `ExternalImageReader`
  already reads `https`.

## Behaviour

### Connecting to a store

- **A development store in the author's own Shopify organisation**, with an app
  created in the **Dev Dashboard**. Since 2026-01-01, new custom apps can no
  longer be created in a store's admin
  ([Shopify changelog](https://changelog.shopify.com/); apps created before
  that keep working).
- **Authentication by the client-credentials grant.** The app's client id and
  secret are exchanged at the store's OAuth endpoint for a token that lasts
  24 hours. The adapter caches the token and asks again before it expires
  ([Shopify](https://shopify.dev/docs/apps/build/dev-dashboard/get-api-access-tokens)).
  This works only for stores in the same organisation as the app.
- **A real merchant's store is outside our organisation.** It needs the
  authorization-code grant and an install. That is case A for real, and it
  waits for S2 (*Non-goals*).
- **Credentials are secrets**: the store's domain, the client id and the
  secret. They are AppHost parameters marked secret, and they live in user
  secrets on the author's machine. They never go in the repository.
- **Scopes**: read-only for the import (`read_products`, plus what translations
  and locales need, confirmed when the app is created). The seeding script also
  needs `write_products`, and only the script uses it (decision 7).
- **The API version is pinned** to the current stable one, checked on the day
  the adapter is written, and recorded beside the client.

### Reading

- **One bulk operation per import** (`bulkOperationRunQuery`):
  - the products, with their variants, media, options, taxonomy category and
    publication date;
  - polled until it finishes, then its JSONL file read as a stream into
    `ExternalProduct`s.
- **Why bulk:** bulk operations have none of the cost and rate limits single
  queries have, and are what Shopify recommends for reading a whole catalogue
  ([Shopify](https://shopify.dev/docs/apps/build/apis/graphql-admin/rate-limits)).
  One path, whatever the store's size, keeps the port's promise that a large
  catalogue is never held whole.
- **How the file is assembled:** a bulk result writes children on their own
  lines with a `__parentId`. The reader groups them as it goes and yields a
  product once its last child has been read. *Found while building `P16-3`*:
  since 2026-01, children no longer follow their parent unless the query asks
  for `groupObjects: true`, which Shopify warns is slower. We ask for it, so
  the file can be read as a stream. A file that is not grouped anyway is
  refused (`UngroupedBulkFile`) rather than guessed at.
- **Translations** are read *in the same bulk query*, one alias per locale
  Tendero speaks besides the store's own (`t_en: translations(locale: "en")`,
  `read_translations` scope). Only `es` and `en` are read, and only when the
  store has published them. This replaces a second query per product, which
  the first draft proposed. The round trip against a real store (`P16-6`)
  confirms the shape.

### Mapping

| Shopify | Tendero | note |
|---|---|---|
| product id (from its `gid`) | `ExternalId` | stable. The handle can change; it is kept for S2's redirects (decision 4) |
| `title`, translated | `Names` | |
| `descriptionHtml`, translated | `Descriptions` | **converted to plain text**, keeping paragraphs (decision 3) |
| `vendor` | `Brand` | |
| taxonomy category | `Category` | through a **mapping file** to our codes. Unmapped means no category, and it is reported (decision 5) |
| lowest variant price, shop currency | `PriceAmount`, `PriceCurrency` | the "from" price Shopify itself shows; each variant's own price travels on the variant (decision 2) |
| `publishedAt` (date) | `AvailableFrom` | null when never published |
| media images, `altText` | `Images` | a variant's own image sets that picture's `Shows` to the variant's options |
| options (`Color`, `Size`) | variant `Attributes` | resolved against the attribute definitions like the seed's words |
| variants | `Variants` | each with its id, SKU, barcode and price (decision 1) |
| metafields under the configured namespace (`tendero`) | `Attributes` | each key an attribute, in the store's words (decision 6, amended) |
| collections, other metafields, `productType`, tags | — | not in S1 (decision 6) |

The shop's primary language is the base. A culture the store does not have is
filled later by the enrichment slice, as the contract already says for a
single-language source.

### Re-importing

As today: the source rules over what the source owns, status is never touched,
and a product is matched by `(source, ExternalId)`. A variant is matched by the
source's variant id, through `VariantExternalReferences`, so a SKU renamed in
Shopify does not create a second variant.

*Corrected 2026-10-23, from the code:* **our SKU is never renamed.** It is the
key Inventory, the cart, orders and UCP speak (`Variant.Sku`), and renaming it
would orphan the stock and the order lines that name it. So a variant matched by
its source id keeps the SKU it was first given. The source's new SKU is reported
as a note (`SkuChangedAtSource`) for a person to decide.

### What the import says beside its counts

`ImportProductsResult` keeps its counts and gains **notes**: a
`Message<TCode>` list (ADR 0059) with a code, what it is about and the
sentence. Possible codes:
- `CategoryNotMapped` (with the Shopify category);
- `LocaleIgnored` (a published locale Tendero does not speak);
- `VariantWithoutSku`;
- `ImageUnreadable`;
- `SkuChangedAtSource` (see *Re-importing*);
- `SkuSharedAtSource`: several variants share one SKU, so each got ours;
- `DescriptionHadLayout`: the HTML held a table or a list whose layout plain
  text loses, so we learn whether real stores need it (the author, 2026-10-23).

*As built (`P16-5`)*: per-product notes travel on `ExternalProduct.Notes`.
The source's own (the languages) travel on `ICatalogSourceConnector.SourceNotes`,
a default interface member, so the seed is unchanged. The handler adds the
two only it can see: `ImageUnreadable` and `SkuChangedAtSource`. The sentence
is in the language the endpoint picks (`?culture=` → `Accept-Language` → `es`).

So a person can see what did not come across, product by product, instead of
reading logs. The OpenAPI document and the frontend types are regenerated.

### The degraded path

- **No credentials configured:** the `shopify` source is not registered. Asking
  for it is the existing 400, which names the registered sources. Nothing else
  changes, and a fresh clone runs exactly as today.
- **Shopify unreachable, throttled or the bulk operation fails:** the import
  fails with a coded refusal (ADR 0059) and writes nothing half-way. A product
  that fails to map counts as `Failed`, as today, and the rest go in.

## Testing

- **The contract suite**, inherited (`CatalogSourceConnectorContractTests`),
  against the adapter reading **recorded fixtures**: a bulk JSONL file and the
  translation answers, committed. No network in CI, and the same answers every
  run.
- **The reader's own tests**:
  - children after their parent, a product with no variants;
  - a variant with no SKU;
  - HTML descriptions;
  - a locale Tendero does not speak;
  - an unmapped category;
  - the token refreshed before it expires.
- **One integration test against the real development store**, skipped without
  credentials, like the Testcontainers tests without Docker.
- **The round trip, the measure that matters** (decision 7):
  - a script fills the development store from Tendero's own seed: 120 products,
    es and en, variants and pictures;
  - the import then reads it back;
  - the imported catalogue must match the seed field by field, except what
    Shopify cannot hold, which the spec lists as it is found;
  - SearchEval's NDCG@10 over the imported catalogue must equal the seed's.
  A source that changes search quality on the way in has lost something, and
  this is how we would know.

### The round trip, measured 2026-10-23

The author's development store (`tendero-dev`, plan Basic, EUR, Spanish
default, English published) was filled with the 120 seed products by
`tools/shopify-seed/seed.mjs` and read back through the adapter.
- **Field by field** (`ShopifyRoundTripTests`): equal on every compared field
  for all 120 products. The fields are the name and description in es and en,
  brand, category, price, attributes, variants (SKU, options, price), how many
  pictures and what each shows, and no notes. Hand mutation: with the English
  names dropped, there are 120 differences.
- **Search** (SearchEval `--source shopify`, lexical, on a throwaway
  Elasticsearch 9.5.0): NDCG@10 en 0.768 / es 0.710, recall@50 0.798 / 0.733,
  notice rate 0.460 / 0.444. That is the same as the seed to three decimals,
  and the same query by query.

**What Shopify cannot hold**, named by the test and not compared:
- **The seed's `available_from`.** Shopify's `publishedAt` is when a product
  was published to a sales channel; Shopify sets it, and never in the past.
- **The external id and each variant's key**, which are Shopify's own numbers.
  The handle (the item id, lowercased) pairs the two sides.
- **Where a picture lives**: Shopify's CDN against our files. How many pictures
  and what each shows are compared.

## Non-goals

- **In S2**: price lists, stock per location, customers, order history,
  discounts, gift cards, redirects, bulk publication, and the
  authorization-code grant for a merchant's own store.
- **In S3**: anything written to Shopify, webhooks and incremental sync.
- **Later or never**: collections as categories, metafields as attributes, more
  than one store at a time, multi-tenancy (analysis §9).

## Measures

- Round-trip fidelity: fields equal, per field, over the 120 products.
- NDCG@10 and recall@50, es and en: imported against seed.
- Time and API cost of one import of 120 products.

## Evidence of use (ADR 0036)

An import is an operator's action, not a shopper's. The proof it is used and
worth it is a server metric, `tendero.catalog.import.products`, counted by
`source` and outcome (created, updated, failed), with the notes counted by code.
- **The question it answers**: is the Shopify source used, and how much of a
  store comes across clean?
- **The decision it would change**: a high count of `CategoryNotMapped` moves
  collections or the taxonomy mapping up the plan.

It goes in the tracking plan with that question.

## Decisions for the author

Each had a proposal. **All nine taken as proposed, 2026-10-23.**

1. **The variant contract grows.** `ExternalVariant` gains the source's
   variant id, its SKU, its barcode and its price, all optional, so the seed is
   unchanged. **Our SKU is the source's SKU when it has one and it is unique;
   otherwise `"{ExternalId}-{Key}"` as today.** *Proposed: yes.* A merchant
   who leaves Shopify keeps the SKUs printed on their labels.
2. **The product's price is the lowest variant price.** Each variant's own
   price travels on the variant. *Proposed: yes.* *Corrected 2026-10-23, from
   the code:* a catalogue `Variant` already has a `Price`, which today simply
   follows the product's. So S1 sets each variant's price from the source when
   the source gives one, and from the product's otherwise, which keeps the
   seed exactly as it is. S2's price lists (compare-at prices, markets) are on
   top of that, in `Pricing`.
3. **HTML descriptions become plain text** with paragraphs kept. The catalogue
   stores text, and every surface (search, Tino, the agents) reads text.
   *Proposed: yes.*
4. **The handle is kept** as an external reference beside the id, unused in
   S1, so that S2's redirects need no second import. *Proposed: yes.*
5. **Categories through a committed mapping file**: Shopify's taxonomy
   category to our code, one file per store, written by a person. Unmapped
   means no category and a note. *Proposed: yes.* Proposing the mapping with a
   model is a later improvement, and it would be a claim a person approves
   (invariant 12).
6. **Collections, metafields, product type and tags stay out of S1.**
   *Proposed: yes.* They are named in the notes if the store uses them, so we
   learn whether they matter. *Amended 2026-10-23 by the author (option A),
   for metafields only:* writing the seed's round trip showed that the seed's
   products carry attributes (colour, material, gender, sizes), and a store
   keeps those in metafields. Without them the imported catalogue would lose
   what search reads ("mujer" lives in the attributes, which was article 03's
   bug), and the round trip could not hold NDCG equal. So **the metafields
   under one configured namespace (`tendero`, `Shopify:AttributesNamespace`)
   come as the product's attributes**, in the store's words, resolved against
   the definitions like the seed's. Collections, product type and tags stay
   out.
7. **The development store is filled from our seed by a script**
   (`tools/shopify-seed/`, Node, as the repository's scripts are), and the
   round trip is S1's measure. *Proposed: yes.* It is the only test that
   proves nothing was lost on the way.
8. **The Admin GraphQL API over `HttpClient` and `System.Text.Json`, with no
   NuGet package.** ShopifySharp is MIT, but it brings Newtonsoft.Json, and we
   use a handful of queries. *Proposed: yes.*
9. **Phase 16 is numbered on the board, with stages S1, S2 and S3**, and
   tasks `P16-1` onwards. *Proposed: yes.* `S-n` is already Steward's.

## Tasks (S1)

- `P16-1` This spec accepted, and the development store and its Dev Dashboard
  app created by the author (the steps are in this spec's *Connecting*) — **S**
- `P16-2` The variant contract and `VariantExternalReferences` (decisions 1
  and 4), with the seed and the contract suite still green — **M**
- `P16-3` The Shopify client: token by client credentials, the bulk operation,
  the JSONL reader, translations; recorded fixtures — **M**
- `P16-4` The Shopify adapter and its mapping, registered only when configured;
  the contract suite inherited — **M**
- `P16-5` The import's notes (`Message<TCode>`), OpenAPI and frontend types
  regenerated — **S**
- `P16-6` `tools/shopify-seed/` fills the development store from the seed; the
  round trip and SearchEval on the imported catalogue — **M**
- `P16-7` The metric in the tracking plan, the notebook, and S1's close —
  **S**
