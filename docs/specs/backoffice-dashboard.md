# Spec: a dashboard that shows the whole shop at a glance

**Status**: proposed (2026-09-29), mock-ups drawn and option B chosen by the author; waiting for the spec's review before the plan (R-9)
**Slice(s)**: the backoffice's dashboard; Ordering (the series and the sales figures), Inventory, Pricing, Search and Accounts for their widgets; `frontend/libs` for the charts
**Article**: *Two engines, one contract* (the chart comparison), and *A dashboard on the wall* (full screen), to be decided with the blog index

## Problem

The author asked on 2026-09-29, after R-8, for a backoffice dashboard "more
professional and with more data" — sales, orders, stock, returns, campaigns,
agents, search — with the chart each figure calls for, as Amazon's Seller
Central, Zalando's partner portal and the large shops' back offices show them.

Today's dashboard (`P11-7`, captured before R-9 in
`docs/blog/reports/2026-09-29-r9-dashboard/screens/before/`) has five widgets
and **no series at all**: every endpoint answers a period's totals, so nothing
on it can draw a bar per day or a trend. Its figures are right and its shape is
a list of numbers. The shopkeeper can read what happened, not how it is going.

## The model: what the references show first

| Back office | First screen | Then |
|---|---|---|
| **Amazon Seller Central** | *Global snapshot*: sales today, open orders, messages, account health against its thresholds | *Business reports*: sales and units by day against the same period before |
| **Shopify admin** | Total sales over time against the previous period, sessions, conversion funnel, average order, returning customers | Top products, sales by channel, live view |
| **Zalando partner portal** | Sales and returns, return rate by reason, stock coverage | Sell-through by article, campaign performance |

Three things they share and this spec keeps: **a snapshot that fits one screen**,
**every figure against the period before**, and **deeper pages per area** rather
than one endless page.

## Decisions taken with the author (2026-09-29)

1. **Option B: an Overview and themed tabs.** Drawn against option A (one page
   with every widget, 2,077 px at 1440); the author chose B. Tabs: *Overview*,
   *Sales*, *Operations*, *Growth*, *Agents*.
2. **Each tab has its URL and a full-screen mode**, so each can sit on a monitor
   of its own (the author: "dado un momento podrías tener cada pestaña en un
   monitor"). `/dashboard`, `/dashboard/sales`, `/dashboard/operations`,
   `/dashboard/growth`, `/dashboard/agents`.
3. **Three layers**, so a widget serves more than one dashboard and a live
   source can replace a request later:
   - **Charts** only draw: they receive series and a format, adapt to their
     container, and do not know where the data comes from.
   - **Widgets** pair a typed source owned by a context with their charts and a
     **size contract** (S, M, L — 3, 6 or 12 columns of a 12-column grid).
   - **Dashboards** are lists of widgets with their sizes.
4. **The chart engine is compared, not chosen**: our own SVG against Apache
   ECharts, behind the same component contract (below).
5. **Real time is R-10**, not R-9, and is compared too: SSE (the author's
   preference) against SignalR, behind a port. R-9's widgets read their data as
   signals so that either can feed them later.
6. **The configurable dashboard (v2) is last in the queue**; the catalogue this
   spec builds is what it will need.

## Behavior

### The Overview (`/dashboard`)

What needs the shopkeeper today and how the week is going, on one screen at
1440 (`3-option-b-overview-1440.png`):

| Row | Widgets |
|---|---|
| 1 | **Key figures** (L): revenue, orders, average order, units, conversion, return rate — each with a sparkline and its change against the period before |
| 2 | **Sales by day** (M) · **Waiting for you** (S) · **Health** (S) |
| 3 | **Campaigns** (M) · **By channel** (S) · **Live** (S, R-10 — not built in R-9) |

### The tabs

| Tab | Widgets |
|---|---|
| **Sales** | Key figures · Sales by day (L, with its table) · By channel · By category · Best sellers (today's list, not redrawn) |
| **Operations** | Orders by status · Health · Checkout funnel · Returns (by reason) · Stock · Waiting for you (`4-option-b-operations-1440.png`) |
| **Growth** | Campaigns (against their baseline) · Customers (new and returning) · Search (top queries, no results, NDCG) |
| **Agents** | What agents buy (share of sales, by day) · Refusals (by reason, the latest) · Trust levels (the registry's counts; not drawn yet) |

The period (24 h, 7 days, 30 days) is one control above the tabs and applies to
every widget; it is in the URL (`?period=week`), so a monitor keeps its period.

### Full screen

- A **Full screen** button beside the period, on every tab at 1440 and wider;
  not on a phone. It uses the browser's Fullscreen API on the tab's own
  element; **Esc** leaves it.
- In full screen there is no menu and no top bar: the tab's name, its period,
  **when it was last updated**, and the way out
  (`6-option-b-operations-full-screen-1920-dark.png`).
- **The tab fills the monitor**: the grid stretches to its width, the rows share
  its height, and each widget spreads its content across the height it is given.
  The type scales with the viewport (a wall is read from further away) within a
  floor and a ceiling.
- Until R-10, a full-screen tab **refreshes every minute**, and says so.
- It is the backoffice as the signed-in person sees it: no kiosk mode, no second
  identity, no token in a URL.

### The size contract

A widget knows its three sizes and chooses what to show from **its own width**
(container queries), never the screen's (`1-widget-catalogue.png`, the last row):

- **S**: the figure, its trend, its change.
- **M**: the chart.
- **L**: the chart and its table, and the comparison with the same period a year
  before once there is one.

**Bars are proportional to their widget, never fixed in pixels** — found while
drawing the full-screen view, where fixed bars stayed short in a wide widget.

### The phone (390)

The Overview stacks its widgets, each at the phone's width; the key figures go to
two columns; the tabs scroll sideways (`7-option-b-overview-phone-390.png`).

### Accessibility

- Every chart has **its figures beside it**: a visible number where the widget
  is small, a table (visible at L, a toggle at S and M) otherwise.
- A chart's marks are reachable by keyboard where they carry a tooltip, and the
  tooltip's text is also the mark's accessible name.
- Colour is never the only carrier: legends are present for two series or more,
  and statuses keep their icon and word.
- `prefers-reduced-motion` turns animations off.
- The accessibility ratchet (`frontend/libs/shared/a11y`) covers every tab, in
  both themes.

### Colour

The charts have a data colour for the first time, validated with the dataviz
method's checks in both themes (lightness band, chroma, colour-blind and
normal-vision separation, contrast), declared in `proposals.json` and proposed
as tokens:

| Token | Light | Dark | For |
|---|---|---|---|
| `--chart-data` | `#2A69B0` | `#5E97CF` | a series |
| `--chart-agent` | `--clay-600` | `#E0633A` | what an agent did (DESIGN.md: agents are clay) |
| `--chart-previous` | `--ink-400` | `--text-muted` | the period before, the baseline |

Statuses keep theirs (`--positive`, `--warning`, `--danger`) and are never a series.

## The charts: one contract, two engines

The components are the contract: `BarChart` (grouped and single), `HBarList`,
`DonutChart`, `Sparkline`, and a `Bullet` against a target. Their inputs are
signals — the series, the format (currency, percentage, count, in the reader's
culture), the labels — and they never fetch.

Behind that contract, for **one chart — sales by day —** two engines:

| | Our own SVG | Apache ECharts 6.1.0 |
|---|---|---|
| Dependency | none | `echarts` (Apache-2.0, tier 1), imported by parts from `echarts/core` |
| Measured weight | to measure | 555 KB minified / 192 KB gzip with bars, line, donut, grid, tooltip, aria and the SVG renderer (2026-09-29) |
| Colours | CSS custom properties (the tokens test sees them) | read from the tokens with `getComputedStyle`, passed in its options |
| Binding | the component itself | a directive of ours (~40 lines, signals), no `ngx-echarts` |

Measured on the same chart: bundle weight, lines of code of ours, accessibility
(the ratchet and a screen reader's reading), the tokens test, keyboard, and the
time to a redraw when the series changes. **One engine stays**; the other is
deleted, and the comparison is kept in the article and the notebook. ECharts
enters as an **experiment with a removal condition** (ADR 0023's idiom), in an
ADR of its own, and leaves the lockfile if it loses.

## The data

Every figure is computed on the server by the context that owns it (the rule
the redesign kept). What exists and what does not:

| Widget | Source | Exists today |
|---|---|---|
| Key figures (totals and change) | Ordering, `GET /api/dashboard/sales`, `/funnel`, `/operations` | yes |
| Key figures' sparklines, Sales by day | Ordering, **a daily series** of revenue, orders and units, for the period and the one before | **no** — the new endpoint R-9 needs first |
| By channel (people and agents) | Ordering, `Order.PlacedBy` | yes; **MCP against UCP is not recorded** on the order |
| By category | an order line does not carry its category | **no** |
| Best sellers | Ordering, `SalesRanking` | yes |
| Orders by status, late | Ordering, the orders list's counts | yes |
| Health (cancellation against target) | Ordering, `/operations` | yes |
| Checkout funnel | Ordering, `/funnel` | yes |
| Returns by reason | Ordering, `/operations` | yes |
| Stock (to restock, sold out) | Inventory, `Restocking` | yes; **coverage in days needs a sales velocity** from Ordering |
| Waiting for you | the existing tasks widget's sources | yes |
| Campaigns against their baseline | Pricing (the window) and Ordering (the sales in it) | **no** — the baseline is new |
| Customers, new and returning by day | Ordering / Accounts | totals yes, **series no** |
| Search: top queries, no results | no aggregate of searches is stored | **no** |
| Search: NDCG | Search, `/api/search/evaluation` | yes |
| What agents buy, by day | Ordering, `/agent-sales` | totals yes, **series no** |
| Refusals by reason | Ordering, `/agent-denials` | yes |

## Evidence of use

The tracking plan's *Shopkeeper dashboard* feature already answers whether the
dashboard is opened, by route. R-9 adds what its own decisions need:

- **Which tabs are opened**: `backoffice_view_opened` with the tab as its view
  (`dashboard`, `dashboard.sales`, …). *Question*: which parts of the shop does
  the shopkeeper watch? *Decision*: a tab unopened for a month folds into the
  Overview or goes.
- **Full screen**: `backoffice_affordance_used` gains `full_screen`. *Question*:
  does anybody put a tab on a wall? *Decision*: if nobody does in a month, the
  wall density and the minute's refresh stay as they are and R-10 is not built
  for walls.

Staff events, under the shop's notice (ADR 0041), never with who.

## Non-goals

- The configurable dashboard (v2): showing, hiding and reordering widgets per
  user. Last in the queue.
- Real time (R-10).
- A dashboard built from arbitrary queries (v3) — that is Grafana.
- Figures from the observability stack (visits, latencies): the dashboard reads
  the shop's own data and does not depend on Grafana being up.

## Open questions

1. **By category.** Snapshot the category into `OrderLine` when an order is
   placed, as the name and price already are (invariant 1: a crossing carries
   values) — or leave the widget out of R-9? Snapshotting is the pattern; it
   needs a migration and it cannot fill the orders already placed.
2. **MCP against UCP.** Record the surface an agent's order came through, on the
   order, or keep *By channel* at people against agents?
3. **Top queries and searches with no results.** Search would keep an anonymous
   daily count per normalised query — no customer, no session — or the widget
   waits. A privacy note (`docs/privacy.md`) comes with it either way.
4. **Stock coverage.** Days of cover need the units sold per day per SKU from
   Ordering: an Ordering read port Inventory's widget asks, or the widget shows
   to restock and sold out only.
5. **The campaign's baseline.** The average of the four weeks before the
   campaign opened, by day — computed by Ordering over Pricing's window.

## Building it, in order

- **D-0**: this spec reviewed; the open questions answered; the plan written.
- **D-1**: the daily series in Ordering (revenue, orders, units, the period and
  the one before), with its contract test and OpenAPI.
- **D-2**: the chart contract and **both engines on *sales by day***, measured;
  the comparison written; one engine removed.
- **D-3**: the other chart components on the engine that stayed; the widgets and
  their size contract; the chart tokens into `tokens.css`.
- **D-4**: the Overview and the tabs, with their URLs and the period in the URL.
- **D-5**: full screen: the button, the wall density, the minute's refresh.
- **D-6**: the phone.
- **D-7**: the widgets whose data the open questions decide.
- **D-8**: the events, the after captures, Stryker, the notebook and the book.

## The mock-ups (2026-09-29)

`design/unpublished/mockups/backoffice-dashboard.pen`, in English, with its PNGs
in `backoffice-dashboard/`: the widget catalogue with the size contract, option
A, option B's Overview (light and dark) and Operations, Operations full screen
on a 1920 × 1080 monitor, and the Overview on a phone. The chart colours are
declared in `design/unpublished/mockups/proposals.json`.
