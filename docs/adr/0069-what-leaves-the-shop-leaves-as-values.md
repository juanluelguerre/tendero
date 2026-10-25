# ADR 0069 — What leaves the shop leaves as values

**Status**: accepted (2026-10-25, phase 16, `E-1` to `E-4`; spec `docs/specs/exports.md`, accepted 2026-10-24 with its five decisions)

## Context

Phase 16 lets a merchant leave Shopify; the exports let them leave Tendero, and sell
elsewhere while they stay: a product feed for Merchant Center, the catalogue as
Shopify's importer reads it, orders for the accountant. The spec put them in a project
of their own, `Exports`, that is not a context — it owns no state — beside `Shopify`,
which is the same thing on the way in.

`Shopify` already answers to a rule: an adapter project never reaches a context's
domain (`An_adapter_never_reaches_a_domain`). The read ports an export would reach for
first do not pass it: `IProductCatalogReader` and `IOrderReader` hand out `Product` and
`Order`, because the slices that use them render or mutate the aggregate.

## Decision

**An export reads each context through a read port of values, made for it, that the
context implements.**

- `Catalog.Ports.ICatalogueExport`: every live product in one culture — names, branch,
  pictures, attributes and each variant's options already rendered, as the product page
  renders them (`CatalogueExportReader`).
- `Ordering.Ports.IOrderExport`: the sales of a span of the shop's days (ADR 0046), the
  dashboard's idea of a sale (`SalesRanking.IsSale`), each order as its invoice says it —
  the billing party, lines, discounts, shipping, tax by rate — and never the buyer's email
  or phone (`OrderExportReader`).
- Inventory's `IAvailabilityReader` was already one.

The rendering stays where the vocabulary is known; `Exports` receives finished values
and may not ask for more. `Exports` joins `Solution.Adapters`, so the rule holds it.

**How a file is written is a keyed port, one per kind of row.** `IExportFormat<TRow>`
writes a slice's rows in one format, registered keyed by its name (`google-merchant`,
`shopify-csv`, `csv`). A slice asks `IExportFormats.Find<TRow>(format)` and holds no `if`
on the format; a new format is a class and a registration line. Every format passes
`ExportFormatContractTests<TRow>`: every row comes out in order, money in its currency's
decimals as a number its reader sums, the file reads back with its format's reader, an
export of nothing is its header, and a hostile text comes back as its words.

**What must leave an audit row is a command; what is fetched by a machine is a query.**
The catalogue and the orders exports are commands, so the audit log says who took them;
the public feed (decision 5) is a query, or Merchant Center's daily fetch would write a
row each time.

## Consequences

- A fourth export reads through a port of values too; if a context has none for it, the
  context gets one, implemented beside its domain.
- The ports are shaped by what leaves, not by a screen, and change when an export
  needs a field: `ExportedVariant.Barcode` exists because a feed's `gtin` does.
- A format's contract is held once, for every format, including the ones not yet
  written (XLSX, when decision 3's evidence comes; a Meta or WooCommerce file).
- The contract suite found its own bug on its first run: the CSV reader opened a quoted
  cell at a quote inside an unquoted one, which a feed writes; RFC 4180 opens one only
  where a cell begins.
