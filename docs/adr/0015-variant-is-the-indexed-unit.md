# 0015 — The variant is the indexed unit; the product is the returned unit

**Status**: accepted

## Context

A product now has variants, and something has to decide what a document in
`products_es` / `products_en` is. Both obvious answers are wrong in different
ways.

**One document per product** loses precision the shop cannot afford. Filtering
`colour = navy AND size = 38` matches a product carrying navy in a 40 and black
in a 38 — the classic false-positive facet, where the shopper clicks through to
a combination that does not exist. Price becomes a range, so "under €30" matches
products where merely *some* variant qualifies and sorting by price is
ambiguous. Stock degrades to "some variant is available", which is how a card
says available and the size you want is not. And an agent gets the worst of it:
UCP cart operations take a **SKU**, so a product-level result forces a second
call and a guess about which variant was meant.

**One document per variant, returned raw** loses everything else. A product with
eight variants floods the top ten. The golden set is keyed on the product's
`externalId`, so it stops being comparable and the committed 0.860/0.720 baseline
is lost for a reason that has nothing to do with relevance. And the vector
ranking is naturally product-level — variants share the name and description
that get embedded, so a per-variant vector would let one product occupy eight
nearest-neighbour slots with near-identical content — which means hybrid search
would have to fuse two rankings of different units.

## Decision

**Index one document per (variant, culture); collapse on `productId` at query
time.**

Matching and filtering happen per variant, so facets are exact. Results come
back as products, so the golden set stays valid and the baseline stays
comparable. The collapsed hit carries the variant that won, so the storefront can
render the card with the matching colour already selected and an agent can add an
exact SKU with no second call.

Each variant document also carries the product's price range, denormalised. It is
constant within the group and it saves the card a second query to say
"24,90 – 29,90 €".

## Consequences

- **`hits.total` counts documents, which are variants.** The product count comes
  from a `cardinality` aggregation on the collapsed field. That is exact at this
  size and approximate above roughly 40,000 groups — a trade to revisit only if
  the catalogue gets there.
- `collapse` has rough edges with deep pagination. Found at six products rather
  than at scale.
- The index grows by the variant count. Irrelevant at laboratory scale, and the
  measured number belongs in the notebook when the full dataset lands.
- Removing a product from the index is a `delete_by_query` on `productId`, not a
  delete by id: how many documents it has is not known from the caller.
- Re-indexing a product **replaces** all of its variant documents. Writing only
  the live ones would leave a discontinued variant in the index forever.
- Verified: after the change, the gate reproduces the committed baseline exactly
  — es 0.860/0.841, en 0.720/0.682. That equality is the whole argument for this
  shape over the raw-variant one, so it is worth re-checking whenever the collapse
  changes.

An earlier draft of `docs/analysis/roadmap.md` recommended product-level roll-up,
weighted mainly by not wanting to invalidate 22 golden-set annotations. That was
a migration concern being used to decide a permanent property of the platform.
Collapse keeps the baseline *and* the precision.
