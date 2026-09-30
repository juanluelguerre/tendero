# 0048 — An order records what the dashboard reads when it happens

**Status**: accepted (the author approved R-9's Plan 3 on 2026-09-30, which proposed it)

## Context

R-9's spec asks the dashboard for figures the large back offices show — sales by
category, by channel (the storefront, an agent over MCP, an agent over UCP), the
late shipment rate against 4 % — and every figure is computed on the server by
the context that owns it. Mapping the code for D-7 found that the order did not
keep three of the facts those figures need:

- **The channel** was known only while the request lasted: `Actor.Surface`,
  resolved from the path and used as a metric's label. An order placed over MCP
  and one placed over UCP's REST leave the same checkout session behind, so
  afterwards nothing told them apart.
- **When it was confirmed and when it shipped**: `Ship()` changed the status and
  `UpdatedAt`, and the moment was gone at the next change. A late shipment is
  measured between the two, and the target R-3 named had waited since for "a
  status history the shop does not keep yet".
- **The category of each line**: the name and the price were snapshotted into
  `OrderLine` (ADR 0002), the category was not, and the category a product is
  filed under can change.

## Decision

1. **The order records each fact when it happens, and never has it guessed
   later.** `Order.Channel` is set at placement from `IActorAccessor`, which
   already answers the surface per request (null for the worker's internal one).
   `ConfirmedAt` and `ShippedAt` are set by `Confirm()` and `Ship()`, beside the
   transition and not instead of it — the transition table stays the single
   source of truth (invariant 5). `OrderLine.CategoryCode` is snapshotted from
   the catalogue through `IPurchasableReader`, as the name is.
2. **An order older than the facts is counted apart, never back-filled.** The
   migration adds nullable columns and rewrites no row. Every figure says what it
   could not count: orders with no channel are their own row and never the
   storefront's, lines with no category theirs, orders shipped before the moments
   were recorded are left out of the late rate and said to be.
3. **Inventory reads the pace through a port of its own.** Coverage and
   sell-through need the units sold, which are Ordering's; `ISalesPace` is
   Inventory's port and Persistence answers it, so Inventory still references
   the shared kernel only (invariant 1).
4. **A campaign's baseline is dates in, figures out.** The backoffice reads the
   window from Pricing's calendar and asks Ordering with it; Ordering never
   reads Pricing for it.
5. **Search's stages are private health checks.** One per stage through its own
   port, a memory of when each last changed, served to the shopkeeper; the open
   `/health` leaves them out (`HealthTags.Private`), because a stage switched
   off is a search that degrades to words, not an unhealthy API.

## Consequences

- The dashboard's figures are honest from the day of the migration and partial
  before it, and say so on screen. The laboratory's orders all predate it: the
  channel and the late rate read as *counted apart* until new orders arrive.
- A new fact the dashboard needs follows the same rule: recorded where it
  happens, nullable, never reconstructed.
- `Stop.Code` stays inside the order's jsonb (the defect rate reads it in
  memory, as every dashboard read does at the laboratory's scale); it moves to a
  column under ADR 0008's rule if it is ever filtered in the database.
