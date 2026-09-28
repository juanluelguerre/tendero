# Spec: a home page that sells (campaigns, and where you left off)

**Status**: proposal agreed with the author (2026-09-29); the mock-ups are next (H-0) (R-8)
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
4. **Puede que te guste** — suggestions from the shopper's own trail, each
   saying why: *Porque viste Mochila CityPack 18L*. Only when there is a trail.
5. **Tendencias** — what is rising this week: the products whose sales grew
   most against the week before, from Ordering's own figures. Different from
   best sellers on purpose: a steady best seller is not news, a rising one is.
6. **Lo más vendido** — the week's best sellers, a row, from Ordering's own
   figure (the dashboard already computes it). Ranked, never with counts.
7. **Novedades** — as today, the row that scrolls.
8. **Secciones de la tienda** — the departments, lower and smaller: the header's
   row already reaches them in one click (R-7.3, F-0).
9. **La tienda promete** — a slim strip: free shipping from 50 €, 14 days to
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

- **Where it lives: Pricing**, beside the promotions it groups — and it is
  **set up in the backoffice, for specific dates** (the author, 2026-09-28): the
  shopkeeper creates a campaign, names it in es and en, gives it its window and
  its priority, chooses the promotions it groups, and can end it early. That
  makes a campaign **Pricing's first writer**: until now Pricing read committed
  files and owned no table, which ADR 0014 names as the reason it was not yet a
  full context. It is one now, with its own lifecycle (Draft → Scheduled →
  Running → Ended, a `TransitionTable`, invariant 5) and its own actor, the
  shopkeeper; the decision is recorded as an ADR before it is built. Promotions
  stay committed files for this slice; a campaign references them by code.
- **An audit row for every change** (who, what, when), as every backoffice
  action has, and the dates are validated where they are set: a window that
  ends before it starts, or in the past, is refused.
- **What it carries**: a code, a name and a line (es/en, invariant 6), a kind
  (`Event` or `Season`) and a scope (the shop, or a category code) — closed sets
  (ADR 0043) — a window, a priority, the promotions' codes, and a theme for the card (which drawing,
  which accent) — never a colour value, a token name.
- **The API**: `GET /api/pricing/campaigns` — the campaigns running now and
  those starting within a horizon (30 days), leading one first (running before
  upcoming, then by priority, then by start). The home draws only the first;
  the whole list is for agents and for whatever lists campaigns later, each with its window, its
  phase (`Upcoming` or `Running`, a closed set — ADR 0043) and what its
  promotions reach; an empty list when none is near. The clock is computed in
  the browser from the window, so a cached answer never shows a wrong
  countdown. The order is the server's rule, not the page's.
- **Two kinds, two scopes.** An **event** (*Tendero Days*, *Black Friday*) is
  short, has a clock and leads. A **season** (*Rebajas de verano*, *Otoño*) is
  weeks long, has no clock, and is what the card shows when no event runs — the
  shop always has a season, so the first screen is never empty for want of an
  event. And a campaign's **scope** is the whole shop or one category: a
  shop-wide one leads the home; a category one (*Semana del hogar*) is the
  banner of that category's pages and never reaches the home. This is how the
  large shops run several at once without two shouting in one place: one
  headline for the shop, the others where they apply.
- **Overlap is a rule, not an accident**: two campaigns may run at once, and
  the engine is unaffected — it still chooses between promotions by their own
  combination rules; the campaigns only decide what the page leads with.
- **The listing**: *Ver las ofertas* opens `?campaign=TENDERO_DAYS`, a listing
  headed with the campaign's name, the products its promotions reach — the same query-time rule as
  *on offer* (R-7.3: a fact that time changes is computed when read), narrowed
  to the campaign's promotions.

### Puede que te guste — suggestions that say why

Differentiator 5 of the vision: explainable recommendations. Each suggestion
names the product in the trail it comes from, and the rule is deterministic
first (invariant 12: what code can decide, code decides):

- **The rule**: products sharing the viewed product's category and its
  attributes (material, colour family, use), ranked by how many they share,
  never a product already viewed, never one sold out.
- **With the vector layer up**: the same candidates re-ranked by the
  similarity of their embeddings, which the hybrid search already computes.
  **Without it, the rule alone** (invariant 8) — and a test takes Qdrant away
  to prove the row still fills.
- `GET /api/search/similar?codes=…` answers with the products and, for each,
  the code it came from: the *why* is data, not wording made up by the page.

### Tendencias — what is rising

Ordering's own figures, no telemetry: units sold per product in the last seven
days against the seven before, the biggest risers first, with a floor (at least
three units this week) so one sale against none is not a trend. With too few
orders to say anything, the row is not drawn — at laboratory scale it may often
not be, and the spec says so rather than faking one.

### Sigue donde lo dejaste — the trail

- **As Amazon does it** (the author asked, 2026-09-29): a guest's trail lives in
  the browser; a signed-in customer's lives in their account, follows them to
  every device, and the guest's trail is merged into it at sign-in. Either way
  the customer can see it, delete it and switch it off.
- **A guest**: viewed products and searches are written to `localStorage` on
  this device and never sent anywhere.
- **A customer**: the trail is kept by **Accounts**, which owns customers
  (a new writer there: `RecordBrowsing`, capped at the last 50 products and 20
  searches, forgotten after 90 days). Signing in sends the browser's trail once
  and clears it locally, so there is one trail, not two. It is personal data:
  the privacy note says what is kept and for how long, and it is read only by
  the customer's own session.
- **Tu historial**, in *Tu cuenta*: the whole trail, removable one by one and
  all at once, and a switch that turns it off — after which nothing is kept,
  not even in the browser. A guest has the same controls on the row itself.
- **Degrades to nothing**: private windows and blocked storage simply show no
  row (the listing's folded column already works this way).

### Agents

- **WebMCP gains `list_campaigns`**: "which events are on or coming, until
  when, and what do they hold". An agent asked "are there deals this week?"
  should not have to scrape a card.
- **The MCP server gains `list_campaigns`**, the same answer as the API.

## Non-goals

- **Not a CMS.** The backoffice sets a campaign's name, line, dates, priority,
  promotions and theme — chosen from the drawings and accents the design system
  has, never uploaded images or free colours. Editing promotions themselves
  stays out of this slice.
- **Not a profile.** The trail is the shopper's own history shown back and the
  suggestions it explains; nothing is inferred about the person, and nothing is
  shared or used for advertising.
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
- `product_viewed.arrived_from` gains `trail`, `campaign`, `suggested` and
  `trending`. **Decision:** if suggestions lead to products no more often than
  the new arrivals do, the rule is not worth its place above them; if the
  vector re-rank does not beat the rule alone, it is switched off.

## Open questions

1. ~~The name.~~ **Decided by the author, 2026-09-29: *Tendero Days*, in both
   languages.**
2. ~~Fixed dates or a recurring window?~~ **Decided by the author,
   2026-09-28: specific dates, set in the backoffice.** The seed carries one
   campaign so a fresh shop has something to show, and the demo creates one for
   this week from the backoffice.
3. ~~One campaign or several?~~ **Decided by the author, 2026-09-28: several,
   taken into account from the start.** Black Friday is therefore a campaign
   of its own beside Tendero Days, not an edition of it.
4. ~~Where the trail lives when signed in?~~ **Decided by the author,
   2026-09-29, as Amazon does it:** the browser for a guest, Accounts for a
   signed-in customer, merged at sign-in, with *Tu historial* to see, delete and
   switch off.
5. ~~An agent-native touch on the home?~~ **Decided by the author, 2026-09-29:
   now.** A card, *Compra con tu agente*, below the campaign: what an assistant
   can do here (search, compare, fill the cart), how to point one at the shop
   (the MCP server's address, the browser's own agent), and that the shopper
   stays the one who pays. Measured by `home_module_opened {module: agent}`.

## How it will be decided

As the filters were: this proposal first, the author's corrections, then
mock-ups in `design/unpublished/mockups/storefront-home.pen` (the home at
1440, a phone, the card before and during a campaign, the trail), then the
author's approval, and only then the build — in the order below.

## Building it, in order

| # | Increment | Size |
|---|---|:-:|
| H-0 | The mock-ups, approved | S |
| H-1 | Pricing's campaigns: the ADR (Pricing's first writer), the aggregate and its lifecycle, persistence and a migration, the window and order rules, `GET /api/pricing/campaigns`, `?campaign=` on the listing | L |
| H-1b | The backoffice: a Campaigns list (ADR 0042's list-and-pane) with create, edit, schedule and end early, each with its audit row | M |
| H-2 | The campaign card — the leading campaign only — with its clock, before and during | M |
| H-3 | The trail for a guest: storage, the row, removal | M |
| H-3b | The trail for a customer: Accounts' `RecordBrowsing`, the merge at sign-in, *Tu historial* with delete and switch-off | M |
| H-4 | Offers as products, best sellers, trends, the promise strip, the new order of the page | M |
| H-4b | *Puede que te guste*: the rule, the vector re-rank with its fallback, `GET /api/search/similar` | M |
| H-5 | Agents: `list_campaigns` in WebMCP and on the MCP server, and the *Compra con tu agente* card | S |
| H-6 | The events, in the tracking plan | S |
