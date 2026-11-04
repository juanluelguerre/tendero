# ADR 0070 — A channel is told; the system of record stays

**Status**: accepted (2026-10-30, phase 16, S3; spec `docs/specs/shopify-channel.md`, accepted the same day with its seven decisions as proposed)

## Context

S1 and S2 brought a Shopify store into Tendero; S3 runs the other way. A merchant
who sells on Shopify keeps its storefront and checkout, and Tendero becomes the
engine behind them: the catalogue, the stock with its reservations, the backoffice.
That needs what the solution did not have — a second place where people buy, kept in
step with Tendero, and orders taken by somebody else arriving in Tendero's order
book. Two copies of anything raise the question this records: which one is true, and
what the other is allowed to do.

## Decision

**Tendero is the system of record; a channel is told.** Each piece of data has one
master, and the other side holds a copy it is sent:

| data | master | the channel holds |
|---|---|---|
| catalogue, a product's status, prices (VAT included, ADR 0028) | Tendero | a published copy |
| stock | Tendero | available minus a margin, decremented by its own sales |
| promotions | the channel (for S3) | its own discounts, applied there |
| cart, checkout, payment, taxes at the till | the channel | — |
| orders | Tendero, once received | where they were taken |
| customers | nobody copies them | each order snapshots its buyer, as every order does |
| fulfilment | Tendero ships | told, so its customer hears it |

- **A context, `Channels`**, holds what each channel has been told: a product's
  listing with the ids the channel gave it, the stock last published, where the
  reconciliation got to. It references SharedKernel only — a product crosses by its
  code, stock by its SKU — and an architecture rule computes it.
- **One outbound port, `ISalesChannel`**, values only, keyed by channel; Shopify's
  adapter implements it and never reaches a domain (the rule ADR 0069 extended to
  `Exports`). The bridge from Catalog's and Inventory's events to Channels' commands
  lives in the worker, as `KnowledgeFromCatalog` did before it.
- **Publishing is events → outbox → the channel** (invariant 7), idempotent by the
  product's code, so a retry or a reconciliation publishes again and changes nothing.
  Draft never reaches a channel's shelf (ADR 0012 on every channel).
- **Stock goes out with compare-and-swap**: the quantity last published is the
  expected one, and a channel that sold since answers *stale* and keeps its number.
  Tendero never overwrites a quantity it did not expect, and never trusts the
  channel's: the sale's order brings the truth. What a channel is told is Tendero's
  available **minus a margin of 2 units per variant**, never below zero, so the
  window between a sale there and its order arriving here cannot oversell by more
  than the margin.
- **Orders arrive by a signed webhook and by a reconciliation**, through Ordering's
  own inbound order port (`IChannelOrderSource`, the `SourceOrder` the import already
  maps) rather than the channel's: a channel is a source too, and one mapping of an
  order is enough. The webhook is a notice — the order is fetched by its id. The
  reconciliation reads what the channel took since its last mark — because the channel guarantees
  neither delivery nor order. Both feed one command, so the same order received
  twice is one. A received order enters the state machine as any order does
  (`Pending` → `PaymentAuthorized`, the payment the channel took), raising
  `OrderPlaced` so stock is held for it; no new status. One that cannot be held is
  received anyway — the money was taken — and flagged *oversold*.
- **A store is a channel only when said** (`Shopify:SalesChannel`, off by default):
  connected to import from, it is never written to by that alone.
- **A channel that would sell at the wrong price is refused at start**: Tendero's
  prices include VAT, so a store that adds it at the till is not connected.

## Consequences

- A second channel is a class implementing `ISalesChannel` and its registration;
  the slices, the bridge and the state do not change.
- Promotions are the channel's while S3 stands. Translating Tendero's campaigns into
  a channel's discounts, or asking Tendero's engine from inside the channel, is a
  later decision with its own evidence.
- The margin is a business number, and `tendero.channel.oversold` is what would move
  it; per variant only if that evidence asks.
- What the agent-native layer gives our storefront — UCP, the agent checkout, Know
  Your Agent — does not reach a channel that owns its checkout. The channel sells;
  the engine stays Tendero's.
