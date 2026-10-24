# ADR 0068 — A rehearsal is the migration minus its writes

**Status**: accepted (2026-10-24, phase 16, S2b, `P16-21`; spec `docs/specs/shopify-leave.md`, *The dry run, then the migration*)

## Context

A merchant leaving a store runs the migration twice: as a rehearsal, which reads
everything and writes nothing, and then for real. The spec measures it by one
sentence: everything the rehearsal said would come did, and nothing else.

Each context's import learnt to rehearse on its own (P16-13 and after). Put five of
them in a row on an empty shop and the rehearsal stops meaning anything: the stock,
the orders and the discounts look their SKUs, categories and buyers up in what the
catalogue's and the customers' imports have stored, and a rehearsal stored nothing.
Every order would be refused for naming a product the catalogue lacks, and every
buyer would be a guest — on exactly the shop the rehearsal exists for.

Two cheaper roads were weighed and set aside by the author. Reclassifying the later
steps' notes afterwards ("the catalogue would bring this one") never builds the
orders, so a refusal that depends on the catalogue's figures would not show.
Importing the catalogue for real first is harmless (it lands in Draft) but the
customers cannot be, since importing them provisions identities.

## Decision

**A rehearsal of the whole migration runs every step in one scope, and the earlier
steps hold what they would bring for the later ones to read.**

- **The catalogue's and the customers' imports, when rehearsing, record what they
  would create** in a store of their own context, scoped to the request
  (`IRehearsedCatalogue` in Catalog: the products as the import builds them and the
  collections it would keep; `IRehearsedCustomers` in Accounts: the origins it would
  bring, each under a fresh id). An import for real records nothing there.
- **The readers the later steps use answer from both**: what is stored and what the
  scope's rehearsal would bring (`RehearsalReaders.cs` in Persistence:
  `ISourceVariantSkus`, `ISourceAddresses`, `ICatalogueVariants`,
  `IImportedCustomerIds`). A product the rehearsal would create is mapped through the
  same code as a stored one (`CatalogueLines`).
- **The host composes the steps** (`StoreMigration`, `POST /api/migration`) in the
  order each needs the one before, because only the host knows all five contexts;
  no context calls another.
- **Anywhere else nothing changes**: a scope with no rehearsal in it holds nothing,
  and each reader answers exactly as the stored one does.

## Consequences

The spec's measure becomes a test: a rehearsal of the fixtures' store on an empty
shop and the migration after it answer the same, step by step, and the migration
stores what the rehearsal counted (`StoreMigrationTests`).

**A new step that reads what another context imports reads it through such a
reader**, or its rehearsal is judged against the shop before the migration. A new
context whose import later steps depend on records its rehearsal the same way.

Ids are the one thing a rehearsal cannot predict: a rehearsed customer has an id of
its own, so a listing compares whether a buyer is named, not which id.
