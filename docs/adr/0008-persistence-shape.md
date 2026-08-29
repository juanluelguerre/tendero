# 0008 — Persistence shape: jsonb for documents, tables for keys

**Status**: accepted

## Context
The aggregates carry structured data of two kinds: values that are only ever
read together with their aggregate (localized text, images, order lines) and
values that are genuinely queried (external references, idempotency keys).
Mapping both the same way makes one of them wrong.

## Decision
- **Localized text is jsonb**, stored as the culture dictionary
  (`{"es": "...", "en": "..."}`) rather than the object, so it reads in psql.
- **Images and order lines are EF complex collections in JSON columns.** EF knows
  their shape, so they stay modelled rather than becoming opaque blobs. Two
  limitations of EF 11 preview.6 shaped the mapping: a complex collection needs a
  CLR type implementing `IList<T>`, so the mapping targets the aggregate's
  backing field and not its `IReadOnlyList<T>` property; and a complex property
  cannot bind to a record's constructor parameter, so `OrderLine.UnitPrice` is a
  scalar `"79.95 EUR"` conversion rather than a nested `Money` type.
- **Product attributes are a jsonb converter** — a dictionary has no complex-type
  equivalent — and the converter restores the `OrdinalIgnoreCase` comparer, or
  `SetAttribute` would silently stop being case-insensitive after a reload.
- **External references are a table** with a unique index on
  `(source, external_id)`: that index *is* the import's idempotency key.
- **One `TenderoDbContext`, three schemas** (`catalog`, `ordering`, `outbox`).
  The contexts share no entities; they share a connection, which is what puts a
  domain event and the state change that raised it in the same transaction.
- **No migrations yet.** In Development the worker creates the schema from the
  model. Migrations arrive with the first schema worth preserving.

## Consequences
Anything stored as JSON is queryable from Postgres but not comfortably from EF.
The first backoffice screen that needs to query order lines relationally is a
migration and an amendment here, not a workaround in a handler.
