# 0007 — Search reads the Catalog domain directly

**Status**: accepted

## Context
`ProductSearchDocument.FromProduct(Product, culture)` projects a catalog
aggregate into a per-language index document. That makes `ElGuerre.Tendero.Search`
reference `ElGuerre.Tendero.Catalog`, the one edge in the solution that does not point
straight down to the SharedKernel.

## Decision
Accept the reference. Search is a **capability**, not a third bounded context:
it owns no state, its indexes are disposable projections rebuilt from Postgres,
and it exists to serve Catalog. Duplicating `Product` into a Search-owned DTO
would add a mapping layer whose only job is to restate the same fields.

The rule that stays hard is the other direction: **Catalog never references
Search.** Catalog raises `ProductUpserted`; the outbox worker calls the
projection. And `Ordering` references neither — orders snapshot product data
(ADR 0002), so no cross-context read is needed there either.

## Consequences
A change to `Product` can break the index shape, which is exactly what the
Verify snapshot of `ProductSearchDocument` is for. If Search ever grows state of
its own (saved searches, query logs), that state is a new context and this ADR
gets revisited.
