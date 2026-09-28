# Spec: a home page that sells (campaigns, and where you left off)

**Status**: draft — the proposal, for the author to read before anything is drawn or built (R-8)
**Slice(s)**: Pricing (a campaign over promotions), Ordering (what sells), the storefront's home, the browser's own memory
**Article**: *A shop window is a query*

## Problem

The author asked on 2026-09-28 for a home "with more wow": offers, a
promotion in the manner of Black Friday — better, one of the shop's own,
*Tendero Days* — in a card that stands out, and *continue where you left off*.

Today the home is a heading, a drawing, four suggested searches and three
rows: the departments, the running offers as five equal tiles, and the new
arrivals. It is correct and it is flat. Nothing on it says *now*: no event,
no deadline, nothing that knows the shopper was here yesterday. A returning
shopper sees exactly what a first-time one does.

## The model: what the large shops put above the fold

| Shop | First screen | Then |
|---|---|---|
| **Amazon** | One campaign hero (Prime Day, Black Friday) with the deal and its clock; "keep shopping for" and "recently viewed" as the second band | Deal rows, "best sellers in", categories |
| **Zalando** | The season's campaign, full width, one call to action | Categories, trending, brands |
| **El Corte Inglés / MediaMarkt** | The event of the week (*Tecnoprecios*, *Días sin IVA*) with its countdown | Offer rows per category, trust strip |
| **IKEA** | The campaign and the new collection | "Your recently viewed", inspiration, services |

Three things recur, and they are what the home lacks:

1. **One event, named, with an end.** A branded event of the shop's own
   (*Prime Day*, *Tecnoprecios*) rather than borrowing the calendar's: a name the
   shop owns is a reason to come back that no competitor has. The clock is the
   point — *Termina en 2 d 4 h* — and before it starts, *Empieza en…* is a
   reason to return.
2. **The shopper's own trail, early.** *Continue where you left off* sits in
   the first or second band on every large shop: it is the single most used
   module for a returning visitor, because it is the one that needs no
   choosing.
3. **Rows that answer "what is worth it here".** Deals with their saving, what
   sells, what is new — each a row that scrolls, with a link to the whole list.

## Behavior

### The page, top to bottom

1. **The campaign** — *Tendero Days*, or whichever runs. Wide, the view's one
   clay action (*Ver las ofertas*), the event's name and line, its clock, and
   what it holds ("hasta −25 % en ropa · envío gratis desde 50 €") from the
   promotions it groups. Before the window: *Empieza en 3 d* and what it will
   hold. **The shop will have several campaigns, and the home shows one at a
   time** (the author, 2026-09-28): the one that leads — a running campaign
   before an upcoming one, then the campaign's own priority. The others wait
   their turn; they are not stacked beside it, because two events shouting at
   once is none. With none near, nothing is drawn and the page starts with
   row 2 — a home never shows an empty stage.
2. **Sigue donde lo dejaste** — the products the shopper viewed and the
   searches they made, most recent first (up to 8 products, 5 searches as
   chips). Only for somebody who has a trail; a first visit does not see an
   empty box. Each can be removed, and one *Borrar* removes all.
3. **Ofertas** — the running offers as a row of products on offer (not tiles
   of promotion names), each card with its saving, and *Ver todas* to the
   listing's `?offer=1`. The promotions' own names become the row's small
   print: they are rules, the products are what is bought.
4. **Lo más vendido** — the week's best sellers, a row, from Ordering's own
   figure (the dashboard already computes it). Ranked, never with counts.
5. **Novedades** — as today, the row that scrolls.
6. **Secciones de la tienda** — the departments, lower and smaller: the header's
   row already reaches them in one click (R-7.3, F-0).
7. **La tienda promete** — a slim strip: free shipping from 50 €, 14 days to
   return from delivery, secure payment. Every number read from the rule that
   holds it (the shipping promotion, `GET /api/returns/policy`), never typed
   into the page.

The heading *¿Qué estás buscando?* and the drawing go: the header's search is
the shop's one search now (the redesign's second walkthrough), and a campaign
is a better first sentence than a question the header already asks.

### Campaigns

A **campaign** is a named window over promotions: *Tendero Days*, 1–7
October, groups `SUMMER25`, `SHIP0` and whatever the shopkeeper adds; *Black
Friday*, 27–30 November, groups `BLACKFRIDAY`. **There are several, from the
start** — the model is a list, never "the campaign": they may follow each
other or overlap, and a promotion may belong to more than one. It is not a new
discount: the promotions keep their rules, the engine keeps
choosing between them, and the campaign only says *these, now, under this
name*. That keeps the combination rules — the part the promotion engine
proves with properties — untouched.

- **Where it lives: Pricing**, beside the promotions it groups, read from a
  committed file (`seed/campaigns.sample.json`) as the promotions are. Pricing
  has no writer yet, and a campaign does not change that (ADR 0014's rule).
- **What it carries**: a code, a name and a line (es/en, invariant 6), a window,
  a priority, the promotions' codes, and a theme for the card (which drawing,
  which accent) — never a colour value, a token name.
- **The API**: `GET /api/pricing/campaigns` — the campaigns running now and
  those starting within a horizon (30 days), leading one first (running before
  upcoming, then by priority, then by start). The home draws only the first;
  the whole list is for agents and for whatever lists campaigns later, each with its window, its
  phase (`Upcoming` or `Running`, a closed set — ADR 0043) and what its
  promotions reach; an empty list when none is near. The clock is computed in
  the browser from the window, so a cached answer never shows a wrong
  countdown. The order is the server's rule, not the page's.
- **Overlap is a rule, not an accident**: two campaigns may run at once, and
  the engine is unaffected — it still chooses between promotions by their own
  combination rules; the campaigns only decide what the page leads with.
- **The listing**: *Ver las ofertas* opens `?campaign=TENDERO_DAYS`, a listing
  headed with the campaign's name, the products its promotions reach — the same query-time rule as
  *on offer* (R-7.3: a fact that time changes is computed when read), narrowed
  to the campaign's promotions.

### Sigue donde lo dejaste — the trail

- **Kept in the browser, not on the server.** Viewed products and searches are
  written to `localStorage` on this device, never sent anywhere: the shop
  does not build a profile, so it needs no consent banner entry, and the
  privacy note says so in one line. A signed-in, cross-device trail is a later
  step (it is personal data, and Accounts would own it).
- **Removable**, one by one and all at once, and it forgets on its own after
  30 days.
- **Degrades to nothing**: private windows and blocked storage simply show no
  row (the listing's folded column already works this way).

### Agents

- **WebMCP gains `list_campaigns`**: "which events are on or coming, until
  when, and what do they hold". An agent asked "are there deals this week?"
  should not have to scrape a card.
- **The MCP server gains `list_campaigns`**, the same answer as the API.

## Non-goals

- **Not a CMS.** The campaign is a file the shopkeeper edits and commits, as
  promotions are; a backoffice editor for campaigns is a later slice with its
  own spec.
- **Not personalization.** *Recommended for you* is the vision's standard AI
  (phase 12b and after); the trail is the shopper's own history, shown back.
- **Not a carousel of banners.** One campaign at a time; a rotating hero is
  the pattern the large shops are abandoning, because nobody sees slide 3.
- **Not stock-pressure copy** ("¡Solo quedan 2!") beyond what the product
  page already states honestly.

## Measures

- The campaigns' windows are tested against the clock (before, during, after,
  the horizon), with two overlapping and the order between them; what each
  reaches reuses `OfferReach` — no second rule.
- The trail: unit tests for adding, deduplicating, capping, forgetting after
  30 days and removal; storage throwing is a test, not a comment.
- e2e: the card with a campaign running and without one; the trail after
  viewing two products and searching once; *Borrar*.
- Accessibility ratchet on the new home at every width; the countdown is text
  a screen reader reads once, not a live region that talks every second.
- The home's weight: the first screen may not grow by more than a measured
  amount (the redesign's budget), and every row below the fold loads lazily.

## Evidence of use

- `home_module_opened {module, position}` — `campaign`, `trail`, `offers`,
  `best_sellers`, `new`, `departments`. **Decision:** a module nobody opens
  in a month moves down or goes; the order of the page follows the numbers.
- `campaign_card_opened {campaign, phase}` — `before` or `during`. **Decision:** if the
  *before* card is not opened, the teaser is not worth its place.
- `trail_cleared {scope}` — `one` or `all`. **Decision:** if many clear it,
  the trail is felt as surveillance; show less, or ask first.
- `product_viewed.arrived_from` gains `trail` and `campaign`.

## Open questions

1. **The name.** *Tendero Days* is the author's; the alternatives the shop
   could own: *Días Tendero*, *La Semana del Tendero*. English and Spanish
   both need a form (invariant 6): *Tendero Days* reads in both.
2. **When it runs in the laboratory.** A campaign with fixed dates is dark
   most of the year. Either the seed carries a campaign that is always near
   (the first week of every month, a recurring window), or a fixed window
   plus a clock the demo can move. The first is simpler and shows the card
   every visit; the second is how a real shop works.
3. ~~One campaign or several?~~ **Decided by the author, 2026-09-28: several,
   taken into account from the start.** Black Friday is therefore a campaign
   of its own beside Tendero Days, not an edition of it.
4. **Where the trail lives when signed in.** Browser-only for now is the
   proposal; say if cross-device is wanted in this slice.
5. **An agent-native touch on the home**: a small card, *Compra con tu
   agente*, pointing an assistant at the shop's MCP server — the project's
   differentiator, visible to a shopper. In, or later?

## How it will be decided

As the filters were: this proposal first, the author's corrections, then
mock-ups in `design/unpublished/mockups/storefront-home.pen` (the home at
1440, a phone, the card before and during a campaign, the trail), then the
author's approval, and only then the build — in the order below.

## Building it, in order

| # | Increment | Size |
|---|---|:-:|
| H-0 | The mock-ups, approved | S |
| H-1 | Pricing's campaigns: the file, the window and order rules, `GET /api/pricing/campaigns`, `?campaign=` on the listing | M |
| H-2 | The campaign card — the leading campaign only — with its clock, before and during | M |
| H-3 | The trail: storage, the row, removal | M |
| H-4 | Offers as products, best sellers, the promise strip, the new order of the page | M |
| H-5 | Agents: `list_campaigns` in WebMCP and on the MCP server | S |
| H-6 | The events, in the tracking plan | S |
