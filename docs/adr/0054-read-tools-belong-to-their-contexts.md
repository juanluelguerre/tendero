# 0054 — Read tools belong to their contexts

**Status**: accepted (S-3b, 2026-10-02, under the author's standing approval of Steward's plans)

## Context

Steward (phase 12) answers the shopkeeper through six reads — products, catalogue
gaps, sales, product performance, stock to restock, returns — each over a query a
context owns. The turn runs in `Assist`, which may reference no other context
(ADR 0052): if it called `ListProductsQuery` or `GetRestockListQuery` itself, it
would learn Catalog's and Inventory's types, and every new read would add a
reference. And what a model may read of the shop is a decision about the shop's
data — which fields, which customer facts never — that belongs to whoever owns the
data.

S-1 had already met the same shape for writes: a context makes its own commands
proposable through `IProposableCommand`.

## Decision

1. **A context makes a read available to a model by implementing `IReadTool`**
   (SharedKernel), through `ReadTool<TArgs, TData>`: a snake_case name, a
   description, the arguments' JSON schema (from `TArgs`, closed sets by their
   names as on the wire), and a read that dispatches the context's own query and
   maps its result into a small DTO of the tool's own (`TData`). It registers it
   in its `Add<Context>`.
2. **The tool's DTO is the boundary of what leaves the shop.** An architecture
   rule walks every `TData` and everything reachable from it, and refuses a
   property that names a customer, an order, an address, a phone or a comment.
3. **A model's mistake is an answer, not a fault.** Arguments that do not parse,
   a query's validator refusing, an unknown warehouse: each becomes an
   `ArgumentException` the agent hands back to the model to correct.
4. **Assist turns reads and proposable commands into the model's tools** — the
   reads wrapped in a `{ data, rows, total }` envelope, so catalogue text arrives
   marked as data, and one `propose_<name>` tool per proposable command. `Assist`
   may reference the model port, `AI`, which is not a context, and nothing else
   besides the kernel.

## Consequences

- A new read for Steward is a class and a registration in its context, like a new
  proposable command.
- The contexts carry a little model-facing code: a description in English and a
  DTO. That is the price of the context deciding what a model may read of it.
- The six tools are also usable by any other agent the shop builds (Tino, phase
  12b) through the same port.
