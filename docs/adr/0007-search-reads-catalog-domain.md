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

## Amendment (2026-09-03, phase 4)

Search now projects **two** contexts: the document's `inStock` comes from
`Inventory` via `StockLevelChanged`. The rule generalises to what it was
protecting all along — *Search may reference any context it projects; no context
may reference Search* — and phase 10 adds a third when `specsText` arrives from
`Knowledge` (see [ADR 0024](0024-the-outbox-is-the-process-manager.md)).

## Consequences
A change to `Product` can break the index shape, which is exactly what the
Verify snapshot of `ProductSearchDocument` is for. If Search ever grows state of
its own (saved searches, query logs), that state is a new context and this ADR
gets revisited.
