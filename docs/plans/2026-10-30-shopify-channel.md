# Plan: Shopify as a sales channel (phase 16, S3) — 2026-10-30

Spec: [`docs/specs/shopify-channel.md`](../specs/shopify-channel.md), accepted by the
author on 2026-10-30 with its seven decisions as proposed (the margin kept at 2).
Branch `feature/shopify-channel`, from `develop`. No commit until the author asks.

## Shape, decided while planning

- **`src/Channels`**, a bounded context (decision 7): `Domain/` (the listing of a
  product on a channel, the last stock published, the reconciliation's mark),
  `Ports/ISalesChannel.cs` (the outbound port, values only), `Features/` (the
  slices: publish a product, publish stock, receive orders' reconciliation). It
  references SharedKernel only, like Pricing and Inventory; an architecture rule
  computes it.
- **The port lives in Channels**, not SharedKernel as the spec's first draft said:
  the context that owns a channel's state owns its port. Shopify's adapter
  implements it (`AddShopify` registers it keyed `"shopify"`).
- **A product crosses by its public code** (ADR 0026), as `ICatalogueExport` hands it
  over; Catalog's port grows a by-code read. The bridge handlers live in the worker
  (`src/Workers/Channels/`), the precedent being `KnowledgeFromCatalog`: they hear
  Catalog's and Inventory's events, read values through those contexts' ports, and
  send Channels' commands.
- **Orders** (P16-26) are received by an Ordering slice, `ReceiveChannelOrder`, fed
  by the webhook and the reconciliation through Channels' port; `Order.Received`
  and `OrderChannel.Shopify` are Ordering's.

## Steps

1. `P16-22` ADR 0070; `src/Channels` with the port and its values; the contract
   suite `tests/SalesChannelContract.cs` linked into Channels.Tests (an in-memory
   fake) and Shopify.Tests (P16-24); Channels in the architecture rules.
2. `P16-23` Channels' domain and persistence: `ChannelListing`, `ChannelStockLevel`,
   `ChannelCursor`; EF configuration in Persistence; the migration.
3. `P16-24` Publishing the catalogue (bridge handlers, `ICatalogueExport` by code,
   Shopify's `productSet` and status, the first publication in bulk, the
   `taxesIncluded` check).
4. `P16-25` Publishing stock (available minus the margin, compare-and-swap, `Stale`).
5. `P16-26` Receiving orders (webhook, HMAC, the id claimed, `ReceiveChannelOrder`,
   `Order.Received`, oversold).
6. `P16-27` Reconciliation and fulfilment back.
7. `P16-28` Metrics in the tracking plan, the round trip on the lab store, S3's close.

## Progress, and what changed on the way (2026-10-31)

Steps 1 to 5 are built and green (no commit). What changed from the spec, each to
be written into it and ADR 0070 at S3's close:

- **Orders come through Ordering's inbound order port, not the channel's** (the
  author's yes, 2026-10-31): `IChannelOrderSource` beside `IOrderSourceConnector`,
  reusing `SourceOrder` and Shopify's tested GraphQL mapping (`UpdatedSinceAsync`,
  `FindAsync`); `ISalesChannel` lost `OrdersSinceAsync` and `ChannelOrder`. The
  webhook is a notice: it reads the order's id and the order is fetched by GraphQL.
  `ReceiveChannelOrders` (Ordering) dedupes by `channel:<channel>:<id>`; the line and
  tax mapping moved below both slices (`Ordering/SourceOrders.cs`).
- **A channel that is down does not retry the outbox**: the bridges count `failed`
  and log, like the other projections; the reconciliation repairs.
- **The first bulk publication is deferred** with its number, measured at the round
  trip (scale rule).
- **Stock is published for drafts too**, ready the day they go on sale.
- **The handle is the code in lower case** (the seed's), not the slug.
- **Oversold** is a column on the order and a `tag--danger` on the backoffice's status
  label; the saga flags instead of cancelling an order a channel took.
- Written down for P16-28: the dashboard's by-channel widget counts Web, MCP and UCP
  only, so Shopify's sales do not show there yet.

Next: step 6 (reconciliation in the worker: orders since the cursor, catalogue and
stock drift; fulfilment back on `OrderShipped`), then step 7.

Each step: the failing test first; tests for existing code proven by a hand
mutation; `dotnet build -warnaserror` and the suites green before the next.
