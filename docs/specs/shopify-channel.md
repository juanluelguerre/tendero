# Spec: Shopify as a sales channel (phase 16, stage S3)

**Status: accepted, 2026-10-30, with its seven decisions as proposed (the margin kept at 2); ADR 0070.**

## As built (2026-10-31)

P16-22 to P16-27 built (plan: `docs/plans/2026-10-30-shopify-channel.md`). What changed
from the text below, each said where it applies:

- **Orders through Ordering's inbound port** (the author's yes, 2026-10-31):
  `IChannelOrderSource` reuses `SourceOrder` and Shopify's tested GraphQL mapping;
  the webhook is a signed notice and the order is fetched by GraphQL. `ISalesChannel`
  keeps publishing, stock and fulfilment.
- **A channel is said, not assumed**: `Shopify:SalesChannel`, off by default
  (`Tendero__ShopifyChannel=true` in the AppHost). A store connected to import from —
  a merchant leaving Shopify — is never written to by that alone.
- **The handle is the code in lower case**, the lab store seed's, not the slug: stable
  on a rename, and publishing updates the seeded products rather than copying them.
- **Stock is published for drafts too**, ready the day they go on sale.
- **Bridges never fail the outbox message**; the reconciliation repairs.
- **The first bulk publication is deferred** with its number.
- **Metafields are written apart, and the attributes travel** (2026-11-04, found after
  the round trip): productSet's metafields replace every one the store holds, and the
  first publication wiped the lab store's attributes. The code and every attribute are
  now written with metafieldsSet, which leaves the others alone, under the key the
  store keeps each attribute under (`Shopify:AttributeKeys`, read from
  `AttributeKeysFile`; the lab store's is `tools/shopify-seed/attribute-keys.json`) or
  the code in lower case. Values as a filter lists them: an option's label, a number
  without its unit, a yes or no as a word in the channel's language. The importer no
  longer reads the channel's `code` as an attribute. An attribute removed in Tendero
  stays in the store until someone removes it there.
- **Oversold** is a column on the order, a tag on the backoffice's status label and
  `tendero.channel.oversold`; the saga flags an order a channel took instead of
  cancelling it.
- **The dashboard's by-channel widget** counts Shopify's buyers with the storefront's
  people and says how many in words.
Case B of [`../analysis/tendero-suite-shopify.md`](../analysis/tendero-suite-shopify.md),
§11: the merchant keeps Shopify's storefront and checkout, and Tendero is the
system of record behind it. S1 brought a store's catalogue in and S2 the rest of
it (case A, leaving Shopify); S3 runs the other way. The article it feeds:
*Shopify in front, your engine behind*.

## Problem

A merchant who already sells on Shopify will not switch storefronts to try a new
engine. What Tendero can offer that merchant is the engine — the catalogue with its
structured attributes, the reasoning layer, the stock with reservations and
warehouses, the backoffice and Steward — with Shopify kept as the place where people
buy. That needs a port the solution does not have: **publishing to a channel**.
Every adapter so far brings data in (`ICatalogSourceConnector` and its five S2
siblings) or writes a file (`IExportFormat<TRow>`, ADR 0069); nothing keeps a second
system in step with Tendero, and nothing receives an order Tendero did not take.

Our own storefront stays the reference channel: the series and the agent-native
claims rest on it. Shopify becomes a second channel fed by the same platform, and
the thesis of the series — one platform, many doors, an adapter per door — gets its
first outbound proof.

## What exists today, measured 2026-10-30

- **Events.** Catalog raises `ProductUpserted` for every change, publishing,
  withdrawing and a price set included, and `ProductArchived` on archiving
  (`src/Catalog/Domain/Product.cs`); there is **no published or unpublished event**,
  so the channel reads the product's status when it hears the upsert. Inventory
  raises `StockLevelChanged(Sku, Available)` on every receipt, count, hold, release
  and commit — **one warehouse's** available, not the SKU's total
  (`src/Inventory/Domain/StockItem.cs`). Price lists are files (`Pricing`) and raise
  nothing; a variant's own price is Catalog's and travels with the upsert.
  Campaigns raise their own events (`Campaign.cs`).
- **The outbox** writes events in the aggregate's commit and `OutboxWorker` runs
  every handler of a message, retrying all of them when one fails (five attempts,
  then the row keeps its error). Every handler must therefore be idempotent, as the
  stock saga's already are.
- **The Shopify client** (`src/Shopify/ShopifyAdmin.cs`) reads with bulk operations,
  retries `THROTTLED` and 429 five times, pins API **2026-10** and holds an offline
  token or client credentials. Its only mutation in .NET is starting a bulk query.
  Every write so far is Node, in `tools/shopify-seed`: `productSet`,
  `metafieldsSet`, `publishablePublish`, `inventorySetQuantities @idempotent`,
  `orderCreate` — proven against the lab store, and the shape the .NET adapter will
  take.
- **Ids.** Products and variants already carry `ExternalReference(Source,
  ExternalId, Handle)` from S1, and `ShopifyOptions.Locations` maps Shopify's
  locations to Tendero's warehouses.
- **Inbound webhooks.** One exists: `POST /api/payments/{provider}/webhook`,
  anonymous, raw body, HMAC-SHA256 with a five-minute tolerance; a replay is caught
  by the aggregate (capturing twice is a no-op), not by a store of event ids. The
  idempotency store (`IIdempotencyStore`, claims by primary key, `Fresh | Replayed |
  Mismatched | InFlight`) is the checkout's and UCP's.
- **Orders.** `OrderChannel` is `Web | Mcp | Ucp`. Imported orders (ADR 0066) are
  born closed and raise nothing; a received order is not history and must take the
  live path, so stock is held for it.
- **Stock** has `OnHand`, `Reserved` and `Available`; a hold is taken on
  `OrderPlaced` and the decrement happens on `OrderShipped`. **No safety margin
  exists anywhere.**
- **Taxes.** Tendero's prices include VAT (ADR 0028). The lab store says
  `taxesIncluded: false` (notebook, P16-16): a store set that way would sell every
  price Tendero publishes 21 % above what Tendero means.
- **The rule that shapes the design**: an adapter never reaches a domain
  (`ArchitectureRules.An_adapter_never_reaches_a_domain`, Shopify and Exports
  held). A Shopify-assembly handler of `ProductUpserted` would fail it. The
  precedent for the bridge is `src/Workers/Knowledge/KnowledgeFromCatalog.cs`: a
  handler in the worker translates an event into values for another port.

What Shopify guarantees, checked on 2026-10-30 against its documentation
([HTTPS webhooks](https://shopify.dev/docs/apps/build/webhooks/subscribe/https),
[best practices](https://shopify.dev/docs/apps/build/webhooks/best-practices),
[`inventorySetQuantities`](https://shopify.dev/docs/api/admin-graphql/latest/mutations/inventorySetQuantities)):
a webhook carries a base64 HMAC-SHA256 of the raw body, keyed by the app's client
secret, in `X-Shopify-Hmac-SHA256`; duplicates are detected by
`X-Shopify-Webhook-Id`; a delivery has one second to connect and five to finish,
and a failure is retried 8 times over 4 hours, after which an Admin-API
subscription is deleted; **ordering is not guaranteed and delivery is not
guaranteed**, so Shopify itself recommends a reconciliation job over `updated_at`.
`inventorySetQuantities` sets absolute quantities with compare-and-swap
(`changeFromQuantity` from 2026-04; a mismatch is `CHANGE_FROM_QUANTITY_STALE`),
requires an idempotency key from 2026-04, and is meant for the system that is the
source of truth for inventory — which here is Tendero.

## Who owns what (ADR 0070, to write with the decisions)

| data | master | in Shopify | how it travels |
|---|---|---|---|
| catalogue: products, variants, attributes, translations, pictures | **Tendero** | a published copy | event → outbox → channel |
| a product's status | **Tendero** (Active is on sale) | active / draft / archived | the same |
| prices | **Tendero** (the variant's price, VAT included) | a copy | with the product |
| stock | **Tendero**, published as available minus a margin | a copy Shopify decrements on a sale | event → outbox → channel, compare-and-swap |
| promotions | decision 3 | | |
| cart, checkout, payment, taxes at the till | **Shopify** | | |
| orders | **Tendero** once received; Shopify where they were taken | | webhook + reconciliation → `ReceiveChannelOrder` |
| customers | decision 4 | | |
| fulfilment | **Tendero** ships; Shopify is told | | decision 5 |

## Behaviour

### The port

`ISalesChannel`, in `Channels/Ports` — the context that owns a channel's state owns its
port (the first draft put it in SharedKernel) — **values only** — the shape ADR 0069 gave the exports:

- `PublishAsync(ChannelProduct product)` — create or update, idempotent by the
  product's id; a product that is not Active is sent as not on sale (draft in
  Shopify), an archived one as archived. Answers the channel's ids for the product
  and each variant.
- `SetAvailableAsync(ChannelStock stock)` — the absolute quantity per SKU and
  location, with the quantity last published as the expected one (compare-and-swap)
  and an idempotency key. Answers `Set`, `Stale` (the channel moved since: it sold
  something) or a refusal.
- `MarkShippedAsync(ChannelShipment shipment)` — fulfilment back (decision 5).

*As built*: orders are not this port's. They arrive through Ordering's inbound
order port, `IChannelOrderSource` beside `IOrderSourceConnector`, as the
`SourceOrder` S2 already maps (see *As built*, below).

Adapters register keyed by channel (`"shopify"`), invariant 3; a fake channel with
the same contract suite proves the port without a store
(`SalesChannelContractTests`, as `CatalogSourceConnectorContractTests` proves the
inbound one).

### Publishing the catalogue

A handler in the worker hears `ProductUpserted` and `ProductArchived`, reads the
product as values through a read port of Catalog (`ICatalogueExport` grows a
by-id read, so the feed and the channel read one shape) and calls each configured
channel. Handlers are idempotent: publishing the same values twice changes nothing
in Shopify (`productSet` by the product's handle and Tendero's id in a metafield).

- **Draft never reaches the shelf**: a Draft product is sent as draft or not at all
  (ADR 0012 holds on every channel).
- **The first publication** of a whole catalogue goes one product at a time, like
  every later change; one bulk mutation (`bulkOperationRunMutation` with `productSet`
  lines) is deferred with its number, measured at the round trip (scale rule).
- **Prices** travel with the product. A price list read from files raises nothing,
  so a change to one reaches the channel at the next reconciliation (below).

### Publishing stock

A handler hears `StockLevelChanged`, reads the SKU's available per warehouse
through `IAvailabilityReader`, maps warehouses to Shopify locations
(`ShopifyOptions.Locations`) and publishes **available minus the margin**, never
below zero (decision 1). The last quantity published per SKU and location is kept,
because it is the expected value of the next compare-and-swap.

`Stale` is not an error. It means Shopify sold something since the last
publication; its order is on the way (webhook or reconciliation), receiving it
holds the stock, the hold raises `StockLevelChanged`, and the next publication is
computed from Tendero's truth. The channel never overwrites a quantity it did not
expect, and Tendero never trusts Shopify's.

Shopify's inventory policy is set to **deny** selling at zero: overselling is
bounded by the margin and the delay, not left open.

### Receiving orders

`POST /api/channels/shopify/webhooks`, `AllowAnonymous` by explicit decision
(invariant 10's rule), raw body:

1. The HMAC is checked in constant time against the client secret, before the body
   is parsed; a bad one is 401, never logged with its body.
2. *As built*: no webhook id is claimed — the order's own key,
   `channel:shopify:{id}`, makes a duplicate delivery and the reconciliation one.
3. The topic `orders/create` is a notice: its id is read and the order fetched by
   GraphQL through the mapping the reconciliation uses, then `ReceiveChannelOrder` — the slice's command —
   inside the five seconds: the order is created, nothing more. Stock is held by
   the existing saga on `OrderPlaced`, through the outbox (invariant 7).
4. Any topic it does not handle answers 200 and is counted, never retried by
   Shopify into deletion.

**A received order** (decision 2) snapshots its lines, prices, taxes and address
as Shopify charged them — Shopify's numbers are the receipt — with
`OrderChannel.Shopify` and `OrderOrigin("shopify", id, number)`, keyed
`channel:shopify:{id}` so a webhook and a reconciliation of the same order make one.

**An order Tendero cannot hold stock for** — Shopify sold past the margin — is still
received: the money is taken. It is flagged *oversold*, shown in the backoffice's
orders to act on, and counted. Refusing it would hide the problem the margin exists
to bound.

**Reconciliation**, in the worker: every 5 minutes the orders changed since each
channel's mark, through the same command (a channel seen for the first time starts
now); at start and every 6 hours the whole catalogue and stock told again — what did
not change is not sent twice — repairing a price list changed, a webhook lost or a
publication a channel refused while it was down. This is the path that works on a laptop Shopify cannot reach (below).

### The degraded path

Shopify down, throttled or misconfigured never touches Tendero's own storefront: the
channel's bridges never fail the outbox message — the projections beside them would
be replayed — so a failure is counted (`outcome=failed`) and the reconciliation
repairs it
(invariant 8's rule, generalised: the shop sells without the channel). A store
whose `taxesIncluded` is false is refused at start, with the reason, before
anything is published.

## Testing

- `SalesChannelContractTests`, abstract, run by the fake channel and by the Shopify
  adapter over recorded GraphQL answers: publish twice is one product; a draft is
  not on sale; compare-and-swap answers `Stale` on a moved quantity; an archived
  product is archived.
- The webhook: a signed fixture accepted, a body changed by one byte refused, a
  replayed id answered once, an unknown topic accepted and ignored; HMAC compared in
  constant time.
- `ReceiveChannelOrder`: Shopify's totals snapshotted, the same order through the
  webhook and the reconciliation made once, an oversold order received and flagged.
- The stock handler: available minus margin, never below zero; `Stale` publishes
  nothing and waits.
- **The round trip on the lab store**, measured and written down like S1's: the
  seed's 120 products published, an order placed in Shopify with its test gateway,
  the order received by reconciliation, stock held in Tendero and the new
  available published back.
- Architecture: the Shopify assembly still reaches no domain; the bridge handlers
  live in the worker.

**A webhook needs a public URL**, which a laptop does not have. In the lab the
reconciliation is the path that runs; the webhook is proven with signed fixtures,
and against the real store only through a tunnel if the author wants one (decision
6).

## Non-goals

- Tendero inside Shopify's theme — Tino, the measured search (S4).
- Approved claims as metafields (S5), and more than one store (multi-tenancy, §9).
- Shopify Markets, several currencies, B2B catalogues.
- Returns and refunds taken in Shopify (`refunds/create`): written down for after
  S3; a received order is cancelled or shipped in Tendero.
- Payments: Shopify took them; Tendero records that it did.

## Measures

Recorded at the round trip, not optimised: the first publication's time and
calls, a product change's time to reach Shopify, a sale's time to come back
through reconciliation, and Shopify's query cost per operation.

### Measured at the round trip (2026-11-03, the lab store)

| measure | value |
|---|---|
| First publication | 120 products in **111 s** (about 0.9 s each, one at a time), 0 refused; 18 drafts reached the store as `DRAFT` |
| Calls per product | 1–4 reads to find it (the listed id first, so 1 after the first publication), `productSet`, and `publishablePublish` for one on sale |
| Stock | 244 levels (every listed SKU at both warehouses), the first 196 in 94 s; none stale |
| An order, Shopify → Tendero | **3 min 27 s** (± 10 s), by reconciliation every 5 min; the mark left at the order's time |
| A shipment, Tendero → Shopify | **4 s** from the backoffice's *Ship* to `fulfillmentCreate`, with Shopify's email to the customer |
| A sale's stock | Barcelona marked stale when the order arrived, set again by the next publication: 4 on hand, 1 reserved, margin 2 → Shopify 1 |
| Query cost (reads) | `product(id:)` 1 point; the SKU search over 250 variants 24 requested, 2 actual; `productByIdentifier` 1; `shop { taxesIncluded }` 1 — against a bucket of 2,000 restoring 100 a second |

A product change goes through the same outbox path as the shipment; its latency was
not measured on its own. The mutations' cost was not read.

**The bulk publication stays deferred** (scale rule): the whole catalogue takes
under two minutes, once, and the reads cost a few points against a bucket of 2,000,
so the limit is latency, not Shopify's budget. It is worth building only when a
catalogue's first publication takes longer than a shopkeeper would wait — at this
pace, some thousands of products.

**Found by the round trip**, each fixed with a test first (notebook item 41): the
reconciliation registered before the migrator; `productOptions` required for a
product with no axes; a second copy of a product whose SKUs had changed since the
seed — identity is now the listed id, the imported id, the SKUs, then the handle,
and two holders of the SKUs are refused; an order for a published product skipped
because only imported variants were mapped; listed SKUs with no stock row left at the
store's own numbers, now 0; `files: null` sent as a value.

**Open, for the author**: an order the reconciliation skips is never read again,
because the mark moves past it.

## Evidence of use (ADR 0036)

| signal | the question | the decision its value changes |
|---|---|---|
| `tendero.channel.publications` (channel, kind, outcome) | Does the channel keep up, and how often does it refuse? | A refusal rate above a few in a hundred means the mapping loses data — fix it before adding a second channel |
| `tendero.channel.orders.received` (channel, path: webhook / reconciliation, outcome) | Do orders arrive, and by which path? | Orders found only by reconciliation mean webhooks are failing: the subscription or the URL, not the code |
| `tendero.channel.oversold` (channel) | Does the margin bound overselling? | Any oversold order raises the margin, or shortens reconciliation |

## Decisions for the author

1. **The stock margin.** A number of units per variant, held back from what
   Shopify is told. *Proposed*: one shop-wide default of **2**, overridable per
   variant later only if the evidence asks; zero allowed. The lab has two
   warehouses and small quantities, so the margin is visible in every test.
2. **How a received order enters the state machine.** *Proposed*: a new factory,
   `Order.Received`, entering `Pending` and moving at once to `PaymentAuthorized`
   with an external payment (*taken by Shopify*), raising `OrderPlaced` so the saga
   holds stock as for any order, then the usual path to `Confirmed` and `Shipped`.
   No new status. The alternative, a status of its own, widens every switch for
   nothing the shopkeeper does differently.
3. **Promotions in the channel.** (a) Leave them in Shopify: the channel sells at
   Tendero's prices and Shopify's own discounts apply there; (b) translate the
   campaigns that fit into Shopify discounts; (c) Shopify Functions asking
   Tendero's engine. *Proposed*: **(a) for S3**, written in the ADR; (b) once a
   campaign's shape is known to fit, (c) never without a measured reason.
4. **Customers.** *Proposed*: **not synchronised.** A received order snapshots its
   name, email and address, as every order does; `Accounts` holds no Shopify
   customer, so personal data does not live twice (§11, "what is genuinely hard",
   3).
5. **Fulfilment back to Shopify.** When Tendero ships a received order, tell
   Shopify (`fulfillmentCreate`), so its customer gets Shopify's shipping email.
   *Proposed*: **yes**, in S3, through the same port (`MarkShippedAsync`) on
   `OrderShipped`; without it the Shopify order stays unfulfilled forever.
6. **A tunnel for webhooks in the lab.** *Proposed*: **none by default** — the
   reconciliation is the lab's path and the webhook is proven with signed fixtures.
   A tunnel is a tool to install (cloudflared, Apache-2.0, tier 2: a process beside
   the app) and to ask for; the round trip does not need it.
7. **Where the channel's state lives** — the ids Shopify gave, the last quantities
   published, the reconciliation's mark. Not in Shopify's adapter (an adapter owns
   no table, like Exports), and not in Catalog or Inventory (their aggregates would
   learn about Shopify). *Proposed*: a small **`Channels` context** — it has a
   writer (the channel's handlers), a lifecycle (published, stale, refused) and an
   actor (the shopkeeper who connects a channel), which is what CLAUDE.md asks a
   context to earn; it references SharedKernel only. The alternative is a table
   owned by Persistence with no context, which hides state the backoffice will want
   to show.

## Orders held, never lost (P16-29) — *accepted 2026-11-04 with its three decisions as proposed; built the same day*

### The problem, found by the round trip

Shopify charges at its checkout and Tendero ships, and only the reconciliation (or
the webhook) joins the two. `ReceiveChannelOrders` skips an order it cannot make into
one of Tendero's, says why in the log and the trace, and moves the mark past it
(`ReadUntil` is the latest change read, received or not). The next pass starts after
it, so **a skipped order is never read again**: paid in Shopify, unfulfilled there,
unknown here. Nobody in the backoffice knows it exists. The webhook path ends the same
way, because it goes through the same command. Only three things bring it back today,
none of them by design: the merchant editing the order in Shopify (its `updatedAt`
moves), the mark rewound by hand, or a second webhook. #1013 was exactly this case,
and was received only after rewinding the mark.

A failure (Shopify down, the database not answering) does not move the mark: the
whole pass is retried. What is lost is what is skipped on purpose.

### Not every skip is the same

| outcome (the slice's own) | why | held? |
|---|---|---|
| `not_open` | cancelled or fulfilled in Shopify before Tendero saw it | **no** — nothing to ship; forgetting it is right |
| `not_in_catalogue` | a line sells a SKU the catalogue does not (a product made in Shopify by hand; #1013 before its fix) | **yes** — mended by publishing or importing the product |
| `no_address` | nowhere to send it | **yes** — mended by correcting the order in Shopify |
| `totals_do_not_add_up` | Shopify's figures do not make an order by our arithmetic | **yes** — a bug of ours, which someone must see |
| `unknown_channel` | the channel is not one an order can come from | **no** — configuration, every order at once: a guard of the slice, not an order to mend |

### Proposed behaviour

- **The mark keeps moving.** One order that is never mended must not hold the window
  open for every other.
- **A held order is recorded** in `Channels` — `channels.held_orders`: the channel, the
  channel's order id and number, the outcome as an enum (ADR 0043), the sentence, when
  it was first held, when it was last tried. Keyed by channel and id, so holding the
  same order twice is one row.
- **Every pass retries the held orders first, by id** — `ReceiveChannelOrderCommand`,
  the one the webhook already uses, through `IChannelOrderSource.FindAsync`. Received,
  already here, or `not_open` (the merchant cancelled and refunded it in Shopify) →
  the row goes. Still skipped → the outcome and the time tried are updated. Receiving
  is idempotent by `channel:shopify:{id}`, so a retry cannot make a second order.
- **The receiving slice says each skip as a value**, not only a sentence:
  `ChannelOrdersReceived` grows the skipped orders (id, number, outcome, sentence), so
  the worker — which composes Ordering and Channels, neither reaching the other —
  writes the rows. Ordering stays unaware of where they are kept.
- **The backoffice shows them** on the orders screen, above the list, only when there
  are any: "2 orders taken in Shopify could not be received", each with its number,
  the reason in the reader's language (a `Message<HeldOrderReason>`, ADR 0059), since
  when, and a link to the order in Shopify's admin. Read by
  `GET /api/channels/held-orders`, for the roles that see orders.
- **No dismiss button.** A held order is money taken; it leaves the list only by being
  received or by being closed in Shopify. Hiding it in Tendero would make the list lie.
- **Evidence of use (ADR 0036)**: `tendero.channel.orders.held` (channel, outcome) when
  an order is held and `tendero.channel.orders.released` (channel, outcome: received,
  closed) when it leaves. *The question*: does the channel lose orders, and why? *The
  decision*: any held order older than a day is a mapping to fix, not a list to watch;
  `totals_do_not_add_up` at all is a bug.

### As built (2026-11-04)

- `ReceiveChannelOrders` says each skip as a value (`SkippedChannelOrder`: id, number,
  `ChannelOrderSkip`, what it concerns, the sentence) and reads one order by id on a path of
  its own (`ChannelOrderPath.Held`), counted as `path=held`.
- `HeldChannelOrder` in Channels, `channels.held_orders` keyed by channel and id; the
  reconciliation retries the held ones first, then holds what the pass skipped, in one save.
  An order the channel no longer has is released as `closed`, like one cancelled there.
- The order's page comes from the channel: `ISalesChannel.OrderPage`, null by default,
  Shopify's admin for Shopify. Taken when the order is held.
- `GET /api/channels/held-orders` (shopkeeper), each reason a `Message<HeldOrderReason>`;
  the API now composes Channels (`AddChannels`) to read it.
- The band, `backoffice-held-orders`, under the orders screen's hint, composes each
  sentence from the code in the reader's language.

### Decisions (accepted as proposed)

1. **Where the list lives.** *Proposed*: `Channels` — it is what a channel was told and
   what it sent that Tendero could not take; it references SharedKernel only. The
   alternative, Ordering, would keep in Ordering's tables something that is not an
   order.
2. **Where it is shown.** *Proposed*: a band on the orders screen, only when not empty,
   since that is where the person who ships works. The alternative, a dashboard widget,
   is seen by someone who does not ship.
3. **When to stop retrying.** *Proposed*: never by time — until received or closed in
   Shopify. A paid order does not become less paid; at lab scale one read per held
   order every 5 minutes costs a point of Shopify's budget each.

### Testing

- The slice: each outcome says itself as a value; `not_open` and `unknown_channel` are
  never held.
- The worker (integration): a skipped order is held while the mark moves past it; the
  next pass, with the product published, receives it and the row goes; one closed in
  Shopify goes as `closed`; a retry still skipped updates the row and makes no order.
- The endpoint: its policy, the reason in es and en, an empty list when there are none.
- The backoffice: the band appears only with held orders, with the link to Shopify.
- A hand mutation for each test written against the slice that already exists.

## Tasks (S3)

Numbered on from S2's.

- `P16-22` ADR 0070 (who owns what, with decisions 1 to 7), the `ISalesChannel`
  port, its values and `SalesChannelContractTests` with the fake channel — **M**
- `P16-23` The `Channels` context (or what decision 7 chooses): published ids, last
  quantities, the reconciliation's mark; its migration — **M**
- `P16-24` Publishing the catalogue: the worker's bridge handlers on
  `ProductUpserted`/`ProductArchived`, Catalog's by-id read, the Shopify adapter's
  `productSet` and status, the first publication as one bulk mutation; the
  `taxesIncluded` check at start — **L**
- `P16-25` Publishing stock: available minus the margin per location,
  `inventorySetQuantities` with compare-and-swap and an idempotency key, `Stale`
  handled, the deny policy — **M**
- `P16-26` Receiving orders: the webhook (HMAC, the id claimed, the topics),
  `ReceiveChannelOrder`, `Order.Received`, `OrderChannel.Shopify`, the oversold flag
  in the backoffice's orders — **L**
- `P16-27` Reconciliation in the worker: orders since the mark, catalogue and stock
  drift; fulfilment back to Shopify on `OrderShipped` — **M**
- `P16-28` The metrics in the tracking plan, the round trip on the lab store with
  its measures, the notebook, S3's close (Stryker over what it touched) — **M**
- `P16-29` Orders held, never lost: the skips said as values, `channels.held_orders`
  and its migration, the retry by id each pass, `GET /api/channels/held-orders`, the
  band on the backoffice's orders screen, the two counters in the tracking plan — **M**
  (*accepted and built 2026-11-04*, after the round trip)
