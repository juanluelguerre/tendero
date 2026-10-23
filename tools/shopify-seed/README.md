# shopify-seed

Fills a Shopify development store from Tendero's own seed, so the Shopify
importer (phase 16, `docs/specs/shopify-import.md`) can be measured by a round
trip: seed → Shopify → import → compare.

## Before running

A development store and an app from Shopify's Dev Dashboard, in the same
organisation. The app's version has to carry these scopes, and the store has
to approve them:
- `read_products` and `write_products`;
- `read_translations` and `write_translations`;
- `read_locales`;
- `read_files` and `write_files`.

The store is set to EUR, with Spanish as its default language and English
published. The three credentials live in the AppHost's user secrets, never in
the repository:

```
dotnet user-secrets set "Parameters:shopify-store" "<store>.myshopify.com" --project src/AppHost
dotnet user-secrets set "Parameters:shopify-client-id" "<client id>" --project src/AppHost
dotnet user-secrets set "Parameters:shopify-client-secret" "<client secret>" --project src/AppHost
```

## Files

- `shopify.mjs`: the Admin API. The token comes from the client-credentials
  grant, and throttled answers are waited out.
- `propose-categories.mjs`: searches Shopify's taxonomy for each category the
  seed uses, and writes `categories.proposed.json`, the candidates.
- `categories.json`: Shopify's taxonomy category → our category code. A person
  approved it (the author, 2026-10-23). The importer reads it through
  `Shopify:CategoriesFile`, and the seeding script reads it inverted.
- `seed.mjs`: one Shopify product per seed product. Each product carries:
  - the Spanish texts, with English registered as translations;
  - the vendor, the product type and the taxonomy category;
  - its attributes as `tendero.*` metafields;
  - its variants under the SKUs the catalogue already gives them;
  - its pictures, uploaded and attached to the variants they show.

  A product whose handle (its item id, lowercased) exists is skipped.
  `--limit N` seeds the first N; `--delete-all` empties the store first.
- `publish.mjs`: publishes every product to the Online Store channel
  (`write_publications`), so the store's own storefront shows the catalogue.
  It also sets each product's `publishedAt`, which the importer reads as
  *available from*.

- `collections.mjs`: one automated collection per category of our taxonomy, its
  rule the product type (our category code); a department gathers the codes
  under it. Spanish titles, English as translations, published.
- `menu.mjs`: the main menu as our storefront's, department → section →
  category, every item a collection (`write_online_store_navigation`). A menu
  item is a `MenuItem` to the menus and a `Link` to translations.
- `filters.mjs`: metafield definitions for colour, material, gender and
  induction, so the Search & Discovery app can offer them as filters.

The store's look in Tendero's style is `tools/shopify-theme/`.

## The round trip

From the repository root:

```
node tools/shopify-seed/seed.mjs
dotnet test --project tests/Integration.Tests --filter-class "*ShopifyRoundTripTests"
dotnet run --project tools/SearchEval -- --elasticsearch <throwaway es> --source shopify
```

The test compares the store with the seed field by field, and is skipped
without the credentials. SearchEval scores the catalogue read back from the
store with the seed's golden set.
