# ADR 0026 — The URL carries a code; the slug is decoration

**Status:** accepted · 2026-09-03

## Context

The product detail page needed a key, and the first version of
`GET /api/catalog/products/by-slug/{slug}` used the slug. Building it surfaced
two problems that are not about scale, which matters because the project's rule
is that architectural completeness is total and only *data volume* is laboratory
scale. Both of these bite at six products exactly as they bite at 147,000.

**A slug cannot be unique.** It is stored as `LocalizedText` in a `jsonb` column
— `{"es": "camisa-de-lino", "en": "linen-shirt"}` — and Postgres cannot put a
unique constraint on a value inside a JSON object. This is not a missing index;
it is the shape of the data. Two products called "Camisa de lino" produce the
same slug and nothing can stop them, so the lookup had an `ORDER BY` that
*preferred the requested culture* — which is to say it guessed which of two
products the URL meant.

**A slug moves.** `UpdateDetails` regenerates it from the name and overwrites the
old one. The moment a shopkeeper corrects a typo in a product name, every inbound
link, every crawler's index and every bookmark points at a 404. Silently: the
only symptom is traffic that stops arriving.

There was also a third, smaller problem. Because the slug lived behind a value
converter, no expression tree could reach inside it, so the lookup had to be
hand-written SQL — and that SQL cost three failures that no unit test could
reach: PascalCase identifiers against a snake_case schema, an EF Core 11 preview
crash inside `GenerateComplexJsonShaper` over a `FromSql` source carrying a
complex JSON collection, and the `"Value"` column name `SqlQuery` projects.

## The two options, and why the rejected one is written down

### Rejected — a slug table (the Shopify model)

Move slugs to `catalog.product_slugs` with `UNIQUE (culture, value)`, keep a row
per address the product has ever held, mark the current one canonical and the
rest superseded, and disambiguate collisions with a numeric suffix
(`camisa-de-lino-2`) the way Shopify's handles do. Renaming demotes rather than
deletes, so an old URL answers 301 instead of 404.

It works, it is what Shopify does, and it surfaced a genuinely interesting
distinction: **"slug" is two things wearing one name.** *Slugifying* a name is
the product's business — it needs no other product. *Being unique* is the
**catalogue's** business, and an aggregate cannot answer it without loading the
catalogue. That is an invariant which does not fit inside the aggregate it
appears to belong to, and it needs a port, an allocator and a table.

It was rejected because all of that machinery exists to *manage* the two problems
rather than remove them. It is strictly more code than the alternative, and the
alternative makes both problems impossible rather than handled.

### Accepted — a public code in the URL (the Amazon / eBay / Zalando model)

The storefront URL becomes `/p/{slug}/{code}` and the API takes the code alone:

```
GET /api/catalog/products/K7M2QX9P4T?culture=es
```

`Product.Code` is ten characters of Crockford base32, minted once at creation,
unique by constraint, and never regenerated. The slug stays in the URL for humans
and for search engines, and it is decoration: change it freely and the link still
resolves.

**Both problems stop existing.** Uniqueness is a unique index on a `character(10)`
column, which is the most ordinary constraint a database has. A rename does not
touch the key, so no history is needed to keep old links alive.

## Why not the id we already have

`ProductId` is a GUID v7, and neither half of that works in a URL. Thirty-six
characters of noise is the smaller objection. The real one is that v7 carries the
creation timestamp in its leading bits and sorts by it — publishing it would let
anyone order the catalogue by the date each product was added and read off how
fast the shop grows.

## Decision

1. `Product.Code` is the public identifier. Ten characters, Crockford base32,
   generated with a cryptographic RNG, `UNIQUE` in the database.
2. The alphabet excludes `I`, `L`, `O` and `U` — the first three because they are
   what people misread against `1` and `0`; the last because thinning the vowels
   keeps a random string from spelling something a shop would rather not print on
   an invoice. Lookup **forgives** those substitutions and folds case, because
   answering 404 to a correct code typed in lowercase loses a visitor for nothing.
3. The API is keyed on the code and knows nothing about the slug in the
   storefront's URL.
4. The response carries the canonical slug and every culture's slug. The
   storefront compares the slug in its own address bar against the canonical one
   and answers 301 when they differ — so a rename is still an SEO event, without
   anything on the server keeping a history of names.
5. The slug remains `LocalizedText` on the aggregate, generated from the name. It
   is no longer a key, so it needs neither uniqueness nor history.

## Consequences

**The lookup is ordinary LINQ over an indexed column**, and the three
runtime-only failures above went away with the SQL that caused them. There is no
tie-break, because there is nothing left to break a tie between.

**The URL is longer and less pretty.** `/p/camisa-de-lino/K7M2QX9P4T` against
`/p/camisa-de-lino`. This is the real cost, it is the reason the Shopify model
exists, and it is what Amazon, eBay and Zalando all decided to pay.

**hreflang gets simpler.** Every culture's URL shares the code and differs only
in the slug, so the alternates are one lookup and no cross-culture resolution.

**A code is a new thing to keep stable.** It is minted in `Product.Create` and
touched nowhere else; reimporting finds the product by `ExternalReference` and
updates it, so the code survives by construction rather than by care.

**The backoffice still addresses products by `ProductId`.** `POST
/api/catalog/products/{id}/publish` is unchanged: internal operations use the
internal id, the public surface uses the public code. That is the same split
Amazon runs, and the asymmetry is deliberate rather than an oversight.

**The migration that indexed the slug stays in the log.** It was applied, and an
applied migration is not deleted however wrong the idea behind it turned out to
be — the log is append-only so that a database elsewhere can still get from where
it is to where the model is.

## Related

- ADR 0008 — persistence shape: jsonb for documents, tables for keys. This is the
  same criterion reaching the opposite conclusion: the answer was not to move the
  slug to a table, but to stop making it a key.
- ADR 0013 — culture negotiation. The code is culture-independent; the slug is not.
- ADR 0015 — the variant is the indexed unit. The search document now carries the
  code so a result card can build a product URL without a second lookup.
