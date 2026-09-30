# Spec: the redesign, run as a lifecycle — R-0, define before designing

**Status**: built (R-0 to R-3 written 2026-09-27; R-4.1 to R-4.7, R-5.1 and R-5.2 built 2026-09-27/28). The baseline walkthrough ran 2026-09-28 (P1); R-6, P1 again on the redesign, ran 2026-09-30 — qualitative, every task done and two errors found and fixed
**Slice(s)**: `frontend/apps/backoffice`, `frontend/apps/storefront`, `design/`, `docs/telemetry/tracking-plan.json`
**Article**: *A redesign with a before and an after* (working title)

This spec is the redesign's single document. It follows the stages the delivery
plan sets out ("Between 11 and 12"). **R-0** is written here now. **R-1** (audit),
**R-2** (benchmark) and **R-3** (proposal and mock-ups) fill in their sections
before anything is built. **R-6** closes it with the same measurements taken again.

## Problem

The author's brief, 2026-09-27, is that the backoffice has to be *"responsive,
professional, modern, fitted to the screen, easy to use and genuinely new, applying
the most current UX techniques"*. For the storefront, *"I miss a menu or something,
don't I?"* — it has no way into the catalogue except search. Both apps were built
screen by screen over eleven phases, each correct on its own. Nobody has yet asked
whether a shopkeeper gets their day's work done quickly, or whether a shopper can
find what the shop sells without typing. A redesign that only restyles cannot answer
that. So this one starts by saying what "better" will mean, and by measuring
"before".

## Who, and the jobs they come to do

**The shopkeeper** (backoffice), five jobs, the ones the dashboard's
*Waiting for you* and *Orders and health* point at:

| # | Job | Done when |
|---|---|---|
| B1 | Publish a draft product after checking it | the product is Active |
| B2 | Review the weakest claim waiting | it is approved or rejected |
| B3 | Find a confirmed order and ship it | it is Shipped |
| B4 | Decide an open return | it is approved or rejected |
| B5 | Suspend an agent that misbehaves, with a reason | the agent is Suspended |

**The shopper** (storefront), four jobs:

| # | Job | Done when |
|---|---|---|
| S1 | From the home page, reach a named category (*mochilas*, *menaje de cocina*) | its listing is on screen |
| S2 | Find a product they can describe but not name | its page is open |
| S3 | Add a variant to the cart | the cart shows it |
| S4 | Pay as a guest | the order page is shown |

## Goals and signals (Google's HEART, as each surface needs it)

| Surface | HEART | Goal | Signal | Metric | Source |
|---|---|---|---|---|---|
| Backoffice | **Task success** | each job completes without help | a job's completion | success rate per job; **interaction cost** (clicks + keystrokes) and **time on task** | walkthrough (below); `backoffice_view_opened` → `backoffice_action_taken` *(new)* |
| Backoffice | **Happiness** | the jobs feel easy | a rating after each job | **SEQ** (Single Ease Question, 1–7) per job; **SUS** once for the whole backoffice | walkthrough |
| Backoffice | **Engagement** | the day starts on the dashboard | dashboard openings against the queue's | openings per day | `http.server.request.duration` by route (the dashboard's tracking-plan entry) |
| Backoffice | **Adoption** | new affordances are used | first use of each | share of sessions using it | `backoffice_action_taken` with the affordance's name *(new, when R-3 names them)* |
| Storefront | **Task success** | a category is one or two steps away | reaching a listing, and how | interactions to a listing from home; **share of listings reached by navigation vs by search** | walkthrough; `category_opened {arrived_from}` *(built)* |
| Storefront | **Engagement** | browsing leads somewhere | product pages per session | `product_viewed` per session | existing |
| Storefront | **Retention / conversion** | nothing gets worse on the way to the till | carts to orders | conversion | `tendero.carts.created`, `tendero.checkout.attempts` (the dashboard's funnel) |
| Both | **Quality floor** | nothing gets worse for anybody | defects per view | WCAG 2.2 AA failures; horizontal overflow at 360/390 px; Core Web Vitals (LCP, CLS, INP) | audit (R-1); browser telemetry already sends Web Vitals (ADR 0036) |

**Success criteria, agreed before designing** — the targets R-3 designs to and R-6 checks:

- Every backoffice job succeeds with **fewer interactions than the baseline**, and none
  takes more. **SEQ ≥ 6** on each.
- **S1 in at most two interactions** from any page, on a phone and on a desktop.
  Today it cannot be done from the header at all.
- **No page scrolls sideways at 360 px**, except tables that scroll inside their own
  frame on purpose, with the cue the existing tables already use.
- **No WCAG 2.2 AA failure**, found by hand where no tool can find it (see open questions).
- **No regression** in conversion, LCP, CLS or INP beyond the noise of the
  laboratory's traffic.

## Behavior — what R-0 adds before anything is redesigned

1. **Three browser events** join the tracking plan, and a feature ships them only
   with its question and its decision (ADR 0036):
   - `backoffice_view_opened {view}`: the shopkeeper opened a screen. **Built
     2026-09-27, with the one below, as staff events under ADR 0041.**
   - `backoffice_action_taken {view, action, outcome}`, with outcome one of
     `done`, `refused` or `failed`: they did what the screen is for (publish,
     approve, ship, decide, suspend). Time on task is the gap between the two
     within one session. Interaction cost comes from the walkthrough, not from
     telemetry.
   - `category_opened {arrived_from}` in the storefront, with arrived_from one of
     `menu`, `search`, `category`, `home`, `other` or `direct`: how a shopper reached
     a listing. `menu` comes from the menu link's own navigation state; anything
     else is the kind of page before, the same reading `product_viewed` makes.
     Before R-5, `menu` never appears. **Built 2026-09-27.**

   Consent follows the storefront's rules for the shopper. For the shopkeeper,
   these are staff tools and the backoffice has no consent banner, so the two
   backoffice events wait for the author's decision on their basis (open question 2).
2. **The baseline is measured on today's interface**, twice over:
   - **The walkthrough.** The nine jobs above, on the laboratory shop seeded by
     TrafficSim (`--TrafficSim:Seed=42`), at a desktop (1440 px) and a phone
     (390 px) width. For each job it records success, time, interactions, errors
     and SEQ, and SUS once at the end. The author is the participant; a second
     person joins if one is available. At n = 1–2 these are numbers that describe
     a walkthrough, not a population, and the article says so.
   - **The automated half**, repeatable at R-6: scripted Playwright paths for each
     job count the interactions the interface needs, as the minimum possible
     interaction cost. Captures and a horizontal-overflow check run at 360, 390,
     768, 1024 and 1440 px. The dashboard captures of 2026-09-27
     (`docs/blog/reports/2026-09-27-dashboard/`) are the first of these.
3. **The numbers are committed** under `docs/blog/reports/<date>-redesign-baseline/`,
   and their summary goes in this spec. That is the "before" the article compares
   against.

## R-1 — the audit: what today's interface does to the nine jobs

*Run 2026-09-27 on the laboratory stack. Every route of both apps was checked at
360, 390, 768, 1024 and 1440 px, with captures at 390 and 1440 and the machine
checks in `findings.json`, all under `docs/blog/reports/2026-09-27-redesign-audit/`
(private). The script checks what can be checked without a new dependency:
horizontal overflow and what causes it, images without `alt`, controls without an
accessible name, inputs without a label, touch targets under 24 px (WCAG 2.2
2.5.8), and the heading outline. Contrast and visible focus are the by-hand half,
still to do: the accessibility checker is open question 1. Severity follows
Nielsen's scale: 4 is a catastrophe, 3 major, 2 minor, 1 cosmetic.*

**Contrast, measured (2026-09-27, decision 1-A).** Every visible text on every route
of both apps was checked at 360, 768 and 1440 px with WCAG's own formula (4.5:1,
3:1 for large text), composited over its real background: **no failure in either
app**. The tokens were built with their ratios written beside them, and this is
that care showing. The check lives in `libs/shared/a11y`, is held to known pairs
in its spec, and was proven to catch grey on white before its zero was believed.
Both end-to-end suites now hold the interface to R-1 as a **ratchet**
(`accessibility-baseline.json`): 147 backoffice findings (124 small targets, 23
overflows, which are B-F1, B-F2 and B-F8) and 12 storefront ones (small targets,
S-F4). A new finding fails the run, and a fixed one is reported so the baseline
shrinks as R-4 works through it. Keyboard paths and focus order are still the
by-hand half.

**What is already right, and stays.** Every page has exactly one `h1`. The machine
found no image without `alt`, no control without a name and no input without a
label, in either app. No storefront page scrolls sideways at any width. The
storefront's product page on a phone reads well. And the dashboard already
answers "what needs me?" (P11-7).

### Backoffice

| # | Finding | Heuristic / criterion | Sev. | Jobs |
|---|---|---|:-:|---|
| B-F1 | **The tab bar does not fit.** Twelve tabs in one row push every page 120 px sideways at 768 px (a tablet). On a phone the row scrolls with no cue past *Attributes*, and *Review queue* wraps onto two lines. | WCAG 1.4.10 Reflow · N8 aesthetic and minimalist · N6 recognition | **3** | all |
| B-F2 | **The audit page scrolls the whole page sideways on a phone**, by 520 px. Its table is not in the frame the other tables scroll inside. | WCAG 1.4.10 Reflow · N4 consistency | **3** | B5 |
| B-F3 | **Orders cannot be searched, filtered or sorted.** Twenty-four rows, newest first, and short ids. Finding one order means reading the list. | N7 flexibility and efficiency · N6 recognition | **3** | B3 |
| B-F4 | **On a phone, an order's actions are off-screen.** *Ship* and *Cancel* sit in the table's last column, past *Payment*, *Lines* and *Total*; the job needs a sideways scroll per order. | N7 · WCAG 1.4.10 | **3** | B3, B4 |
| B-F5 | **The destructive action has the same weight as the useful one, and does not ask.** *Cancel* sits beside *Ship*, the same size and style, and one click cancels the order, releasing its payment hold. The code confirms it: the move is sent on click. There is no undo, because a cancelled order is final. | N5 error prevention · N3 control | **3** | B3 |
| B-F6 | **Screens speak the developer's language.** Hints read "the state machine decides which move is legal", "Draft is why this queue exists", "a log that leads with successes is a changelog". The command appears as `CompleteCheckoutSessionCommand`, and the heading reads "Audit 13 refused". | N2 match with the real world | **2** | B1, B3, B5 |
| B-F7 | **The empty review queue offers a developer's action as its one clay button**: *Import the sample catalog*. | N2 · N8 | **2** | B1 |
| B-F8 | **Small targets on a phone.** The language switch is 31×19 px, and the widget links are 18 px high. | WCAG 2.5.8 Target size (24 px) | **2** | all |
| B-F9 | **The header wraps on a phone.** *Sign out* breaks onto two lines beside the role tag and the language switch. | N8 | **2** | all |
| B-F10 | **Loading is a sentence**, *Loading…*, where the shape of what is coming would be. | N1 visibility of status | **1** | all |
| B-F11 | **The content does not use the width it has.** Every screen sits in a centred column of fixed maximum width; at about 1360 px nearly 300 px stay empty at the sides, while the staff notice runs edge to edge, so the page looks misaligned. Found by the author, 2026-09-27, after R-1. | N8 · N4 consistency | **2** | all |

### Storefront

| # | Finding | Heuristic / criterion | Sev. | Jobs |
|---|---|---|:-:|---|
| S-F1 | **No way into the catalogue from the header.** The departments exist only as cards on the home page. A breadcrumb leads up from a product; nothing leads across from the cart, the checkout or a search. S1 cannot be done from any page but home. | N6 recognition · N3 control · Baymard (navigation) | **3** | S1, S2 |
| S-F2 | **The phone header spends its space on the brand.** The logo's three-line tagline and the language switch take the row where a menu and search would be. | N8 | **2** | S1, S2 |
| S-F3 | **Checkout asks for a click to learn the shipping.** *¿Cómo quieres recibirlo?* has to be pressed before a total exists, and *Pagar* stays disabled with "Se calcula al pagar" until then. | N1 · N7 | **2** | S4 |
| S-F4 | **Small targets.** *Comparar* on every card is 61×21 px, and *Seguir comprando*, *Tu cesta* and the breadcrumb's *Tienda* are 22 px high. | WCAG 2.5.8 | **2** | S2, S3 |
| S-F6 | **"TIENDA" in the header looks like navigation and is not.** On a desktop the header reads *ES EN · TIENDA · account · cart*, and *TIENDA* is a label (`brand__area`) set in the same row and weight as the links. It is the menu the author missed, promised by the layout and absent from it. | N4 consistency · N2 | **3** | S1 |
| S-F5 | **"Novedades" cuts its last card at 1440 px.** The half card is the only cue that the row scrolls, and there is no control to scroll it. | N1 · N6 | **1** | S2 |

### What R-1 hands to R-3

The findings rank themselves by severity and by how many jobs they touch:
- **B-F1, S-F1, S-F2 and S-F6** are the navigation of both apps, and they are exactly
  what R-2 adopted: P1, the side rail, and S-P1, the category menu.
- **B-F3, B-F4 and B-F5** are the orders list, answered by P3 (filters), P6 (a
  side pane) and a destructive action set apart.
- **B-F2 and B-F8** are fixed in passing.
- **B-F6 and B-F7** are copy, for `design/DESIGN.md`'s voice.

Before R-3, two things remain. The by-hand pass (contrast, focus order and
visibility, keyboard paths, B-F5's confirmation) waits for the checker decision.
The **baseline walkthrough** (R-0) waits for the author, as the participant.

### The baseline's automated half: minimum interaction cost today

The shortest path for each of R-0's jobs, read off the running app on 2026-09-27.
"Interactions" are clicks and taps plus fields filled; "scan" means the job depends
on reading a list that no tool narrows. R-6 counts the same paths on the new
interface. Times and SEQ come from the walkthrough.

| Job | Shortest path today | Interactions | Notes |
|---|---|:-:|---|
| B1 publish a draft | tab *Review queue* → *Publish* | 2 | no drafts in the laboratory today; the path is read from the screen |
| B2 review the weakest claim | tab *Claims* → *Approve* / *Reject* | 2 | the queue is already weakest-first |
| B3 find an order and ship it | tab *Orders* → scan → *Ship* | 2 + scan | no search; on a phone, plus a sideways scroll to reach *Ship* (B-F4) |
| B4 decide a return | tab *Returns* → *Approve* / *Reject* (+ a reason) | 2–3 | |
| B5 suspend an agent | tab *Agents* → *Suspend* → reason → *Suspend* | 4 | |
| S1 reach a category | from home: a department card, 1. From a product: its breadcrumb, 1–2. From search, cart or checkout: logo → card, **2, and only via home** | 1–2 | no route from the header at all (S-F1, S-F6) |
| S2 find a product described, not named | type → *Buscar* → a result | 3 | |
| S3 add a variant to the cart | pick the variant → *Añadir al carrito* | 1–2 | |
| S4 pay as a guest | 6 address fields → *¿Cómo quieres recibirlo?* → an option → *Pagar* | 9 | the shipping click is S-F3 |

### The baseline walkthrough (2026-09-28)

*Run on the tag `redesign/before`, in a worktree beside the redesigned code, with
the shop filled by TrafficSim and prepared by `walkthrough-prepare`. One
participant, the author (P1), desktop first. The results are in
`docs/blog/reports/2026-09-28-redesign-baseline/walkthrough/`.*

| Task | Width | Time | Outcome | Interactions | SEQ | P1's note |
|---|:-:|--:|---|:-:|:-:|---|
| B1 | 1440 | 22 s | done | 2 | 7 | |
| B2 | 1440 | 8 s | done | 1 | 7 | |
| B3 | 1440 | 4 s | done | 1 | 7 | |
| B4 | 1440 | 47 s | done | 3 | 6 | the return's detail falls a little short |
| B5 | 1440 | 25 s | done | 2 | 6 | suspended with a reason, and the list still showed the agent as active |
| S1 | 1440 | 6 s | **not done** | 1 | **2** | |
| S2 | 1440 | 17 s | done | — | 7 | |
| S3 | 1440 | 10 s | done | 1 | — | the cart's count changes, but a short message saying it was added would help |
| S4 | 1440 | 64 s | done | 4 | 7 | |
| B3 | 390 | 7 s | done | 2 | 7 | had to go to, or refresh, Orders |
| S1 | 390 | 4 s | done | 1 | 7 | |
| S4 | 390 | 14 s | done | 4 | 7 | |

**SUS 97.5** (68 is the usual average).

**How to read it:**
- **One participant, and the one who built the shop.** The author decided on
  2026-09-28 not to run P2: apart from the add-to-cart message, the backoffice had
  felt simple, quick and agile, and the gain they expected from the redesign was in
  its navigation. So this is n = 1, by someone who knows every screen. It describes
  that session and nothing wider, and the article says so.
- **A ceiling.** SUS 97.5 and SEQ 7 on most tasks leave the backoffice almost no
  room to improve on those two scales. R-6 cannot show a gain in them, only avoid
  a loss. What can move is what failed or cost: **S1 at a desktop** (not done,
  SEQ 2 — S-F1 exactly, the taxonomy invisible from the header) and the findings
  below.
- **Interactions were counted differently from the automated half.** P1 counted
  decisions, not fields, and started some tasks already on the screen (B2 and B3
  in 1, S4 in 4 against a minimum of 9). R-6 compares P1 with P1, counted the same
  way, and the automated half with itself. The two are never mixed.
- **The second width learned from the first.** S1 at a phone, from the cart, was
  done in 4 s after failing at a desktop. The order of widths was meant to spread
  that learning over two participants, and with one it cannot.

**What the session found**, beyond the numbers:
- **W-1 · B5: a suspended agent showed as active** (severity 3). The server says
  `suspended` in lower case, and the screen compared it with `'Suspended'`. The
  agent was suspended, but its tag stayed *Active* and its *Suspend* button stayed
  there. **The redesign had carried the same comparison** into its six places in
  `agents-page.html`. It is the same mismatch R-4.3 found in the trust select: an
  enum's name on one side and its label on the other. No test crossed that line,
  because the e2e checked the Undo toast, not the tag. Fixed first as a symptom (a
  case-insensitive `isSuspended`, since removed), then as a class the same day by
  [ADR 0043](../adr/0043-a-closed-set-is-declared-in-the-contract.md): every closed
  set in the API is typed as its enum, spelled as its C# name and declared in the
  document, so the generated type is `'Active' | 'Suspended'` and a comparison
  with a value the API never sends stops compiling. Regenerating the types found
  seventeen such comparisons in the two apps. An e2e now suspends an agent, reads
  its row and undoes it (hand mutation, red), and a contract test refuses a field
  named like a closed set that is published as a bare string (hand mutation,
  red).
- **W-2 · S3: nothing says the product was added** (severity 2). Only the cart's
  count changes, in the header, away from the button — and a refusal changed
  nothing at all. **Built as R-5.3 on 2026-09-28**: a notice beside the action, in
  a status region that is always there, saying *added* with a link to the cart,
  or *not added*. It stays rather than fading, as P1's "toast" would have: beside
  the button is where the eyes are, and a message that leaves on a timer is gone
  before a slow reader is done with it (WCAG 2.2.1). It goes when another variant
  is chosen. `cart_notice_followed` measures the link; the e2e holds the notice
  and the link (hand mutation, success read as failure: red).
- **W-3 · B4: the return's detail is thin** (severity 2). To re-check at R-6
  against the returns pane R-4.3 built.
- **W-4 · B3 at a phone: the orders list had to be reached or refreshed**
  (severity 2). R-4.2's *To ship* filter and badge are what R-6 checks it against.

### The second walkthrough, R-6 (2026-09-30)

*Run on `develop` after R-4, R-5 and R-7/R-8, with the same kit, the same nine
tasks in the same order, and the same participant, P1, desktop first and then B3,
S1 and S4 at the phone. **Not recorded in the page**: no times, interaction
counts, SEQ or SUS were written down, so there is no table to set beside the
baseline's.*

What P1 reported, in their words afterwards:
- **Every task done, faster and with fewer interactions than at the baseline**,
  S1 at a desktop included — the task the baseline failed (SEQ 2), now reached
  through the header's departments (R-7).
- **Two errors, both fixed the same day** with a browser test that failed first:
  the checkout's step headings sat across their panels' top edge (a `<legend>` is
  drawn on its fieldset's border), and on a phone the products behind the open
  filter sheet still scrolled (a modal dialog traps the focus, not the scroll —
  fixed once, for every modal in both apps).
- W-3 and W-4 did not come up again: the returns pane and the orders' *To ship*
  filter carried B4 and B3 without comment.

**How to read it.** This is a qualitative after, n = 1, by the person who built
the shop and ran the baseline three days earlier; it can say *no loss and two
errors found*, and it cannot say *how much faster*. The article reports it as
that and nothing more. The automated half — the minimum interaction cost on both
interfaces — is the comparison that stays measurable, and it is the one to run
before the article is written.



*Written 2026-09-27 from the products' own documentation and published research
(sources at the end). Every pattern is judged by the question R-0 set — does it make
one of the nine jobs take fewer interactions, or feel easier, or keep the quality
floor? — and not by whether it is fashionable. **Adopt** goes to R-3's proposal,
**conditional** goes with its condition, **reject** says why.*

### Backoffice

| # | Pattern | Seen in | Verdict | Why, against R-0 |
|---|---|---|---|---|
| P1 | **Navigation grouped by job, in a side rail that collapses** (full on a desktop, icons on a laptop, a drawer on a phone) instead of twelve tabs in one row | Shopify Admin, Stripe, Seller Central | **Adopt** | The dashboard's phone capture already shows the tab row scrolling sideways with no cue (R-1's first finding). Grouping *Catalogue* (review, catalogue, attributes, claims), *Sales* (orders, returns, promotions), *Stock*, *Agents* (registry, audit) and *Insight* (dashboard, search evaluation) turns twelve peers into five decisions. |
| P2 | **A command palette**, Ctrl/⌘ K: jump to any screen, or to an order, product or agent by its id or name, and run the screen's action from there | Linear | **Adopt** | B3 (find an order and ship it) today means opening Orders and scanning. Pasting an order id goes straight to it. Keyboard-first, and invisible to anyone who does not want it; R-0's adoption signal measures it. |
| P3 | **Index tables: search, filters and sort on the list itself** | Shopify Polaris (IndexTable) | **Adopt** | Orders, returns and the review queue are lists of work. A status filter and a search box shorten B1, B3 and B4, which today scroll. |
| P4 | **Bulk actions** on a selection | Polaris | **Conditional** | Only where the decision is the same for every row — shipping several confirmed orders. **Rejected for claims and for publishing**: each is a judgement about one product, and a bulk approve is how a review queue becomes a rubber stamp (ADR 0018). |
| P5 | **Saved views** per user | Polaris, Linear | **Reject for now** | Per-user state is P11-7's v2. At three roles and a handful of users, the filters in P3 cover the same need without a new store. |
| P6 | **A side pane that keeps your place**: open an order or a return beside the list, act, and go on down the list | Linear | **Adopt** | B3 and B4 today leave the list and come back to its top. A pane saves the round trip for every item worked through, which is exactly the interaction cost R-0 measures. |
| P7 | **Optimistic updates** | Linear | **Reject** for state changes | Linear applies them where rollback is safe. Ours are audited domain commands the server can refuse (a 409 from a transition table, a mandate, a rule), and the screen shows the server's own words (ADR 0010's rule). Painting "shipped" before the server agrees would show a refusal as a flicker. Instant feedback comes from P8 and a pending state on the button, not from guessing. |
| P8 | **Skeletons in the shape of the content** instead of "Loading…" text | Polaris, Stripe | **Adopt** | Perceived speed, and a layout that does not jump when data arrives (CLS, R-0's quality floor). |
| P9 | **An opinionated home**: the figures that change what the shopkeeper does, each one a way into acting on it | Stripe ("opinionated beats configurable for most users") | **Adopt** — mostly built | P11-7's dashboard already works this way. R-3 makes each figure a link to the filtered list behind it (P3): *8 to ship* opens orders filtered to *Confirmed*. |
| P10 | **Health with thresholds**: a figure against its target, green, amber or red | Seller Central, *Account Health* | **Adopt** | Late shipments and cancellation rate mean something only against a target. The operations widget gains the target and says when the shop is outside it. |
| P11 | **A few list shortcuts** (j/k to move, Enter to open, the screen's action on one key), shown in the palette and on hover | Linear | **Conditional** | Only on the two lists worked through item by item (review queue, claims). A shortcut nobody discovers is dead code, so the palette lists them and R-0's adoption signal decides whether they stay. |
| P12 | **Undo instead of "are you sure?"** for an action that can be reversed (suspend, then reinstate) | Linear, Gmail | **Conditional** | Only where the domain has the reverse operation and nothing has left the shop yet. A refund, a shipment or a publication is not undoable: those keep an explicit confirmation. |

### Storefront

| # | Pattern | Seen in / research | Verdict | Why, against R-0 |
|---|---|---|---|---|
| S-P1 | **A category menu**: on a desktop, a panel with group headings, one column per department; on a phone, a drawer with an accordion per department and "all of the department" at the top of each | Baymard (2025: 76 % of large shops use mega menus; menus without group headings have 23 % more abandonment); NNg (≤ 28–36 links) | **Adopt** | S1 is impossible from the header today. The taxonomy is three departments and twenty categories, well inside the limits, and every link fits under a heading. |
| S-P2 | **Where am I**: the current department marked in the menu, and the breadcrumb already on category pages | Baymard (95 % of shops fail to show it) | **Adopt** | Cheap, and it is the difference between a menu and a list of links. |
| S-P3 | **Thumb-sized targets** (≥ 44 px) in the drawer, and the menu reachable with the keyboard alone | Baymard mobile; WCAG 2.2 (2.5.8 target size) | **Adopt** | R-0's quality floor. |
| S-P4 | **A sticky header** with search and the menu | Baymard; most large shops | **Conditional** | Only if it does not cost the product page its first screen on a 390 px phone. It is measured in R-3's mock-ups before it is built. |

### What R-2 hands to R-3

**Backoffice:** P1, P2, P3, P6, P8, P9 and P10, plus P4, P11 and P12 with their
conditions. **Storefront:** S-P1, S-P2 and S-P3, plus S-P4 with its condition.
R-3 orders them by how much each moves R-0's numbers, and sizes them.

**Sources:** [Polaris IndexTable](https://polaris-react.shopify.com/components/tables/index-table) ·
[Shopify index table composition](https://shopify.dev/docs/api/app-home/patterns/compositions/index-table) ·
[Why Linear feels fast](https://dev.to/0xgosu/why-linear-feels-fast-local-data-small-updates-and-product-discipline-1m53) ·
[How we redesigned the Linear UI](https://linear.app/now/how-we-redesigned-the-linear-ui) ·
[Stripe dashboard design breakdown](https://www.925studios.co/blog/stripe-dashboard-design-breakdown) ·
[Seller Central Account Health](https://sellercentral.amazon.com/performance/dashboard) ·
[Baymard, homepage and navigation](https://baymard.com/blog/ecommerce-navigation-best-practice) ·
[Ecommerce navigation patterns](https://ecomdesignpro.com/ecommerce-navigation-patterns/)

## R-3 — the proposal

*Written 2026-09-27, before anything is built, so the author can read it and
disagree while a change is still a sentence. It takes R-1's findings and R-2's
verdicts, decides each one, says what it should move in R-0's signals, and orders
the build. The design system does not change: tokens only, one clay action per
view, status always a word as well as a colour. The mock-ups are in
`design/unpublished/mockups/redesign.pen`; they are the sketch, and the Angular
built in R-4 is the source of truth.*

### Four rules the proposal keeps

1. **A screen is shaped by its job, not by its table.** The nine jobs decide what
   a screen leads with; everything else is one step further away.
2. **The server decides, and the screen says so.** No optimistic state changes
   (P7): a pending button, then the server's own words, as ADR 0010 already has it.
3. **Nothing new to install.** The dialog, the drawer and the palette are the
   platform's own `<dialog>` and popover, which give the focus trap, `Esc` and an
   inert background for free. No CDK, no component library: the dependency budget
   stays where it is.
4. **Every new affordance is measured.** `backoffice_action_taken` gains `via`
   (`list`, `pane`, `palette`, `shortcut`, `dashboard`), which is R-0's adoption
   signal naming its affordances. The storefront's menu already reports as
   `category_opened {arrived_from: menu}`.

### Backoffice

**Navigation: a side rail grouped by job** (P1; answers B-F1, B-F9, B-F8). Twelve
tabs become six groups, in the order a working day meets them:

| Group | Screens | Badge |
|---|---|---|
| Home | Dashboard | — |
| Catalogue | Review queue · Products · Claims · Attributes | drafts waiting, claims waiting |
| Sales | Orders · Returns · Promotions | orders to ship, returns open |
| Stock | Stock | low-stock lines |
| Agents | Agents · Audit | refusals today |
| Insight | Search quality | — |

- **Widths.** At 1200 px and wider the rail is full (labels, 232 px). From 768 to
  1199 px it is icons with the label as a tooltip and as the accessible name
  (64 px). Under 768 px it becomes a drawer behind a *Menu* button, as a modal
  `<dialog>`.
- **Badges** are the dashboard's own *Waiting for you* figures, read from the
  endpoint that already serves them. A badge is a number and a word for screen
  readers ("3 waiting"), never colour alone.
- **The header** holds the page title and the palette's trigger (*Search or go
  to… Ctrl K*). The language, the role and *Sign out* move into an account menu,
  so the phone header is one row.
- The awning stripe marks the current screen in the rail, which is what it
  marked on the tab.
- **The content area is fluid** (B-F11). It runs from the rail to the window's
  edge, and the dashboard's cards fill as many columns as fit: three when wide,
  two on a laptop, one on a phone. Only running text keeps a maximum measure,
  such as a screen's description or a pane's notes, because long lines read badly.
- **The staff notice (ADR 0041) stays at the top**, under the header and inside
  the content column, with its width and margins. At the foot it would be easy
  to miss, and a notice that is missed weakens the basis it exists for. It shows
  once. The permanent state, and the switch, are the line at the foot of every
  screen, as today.

**The lists of work: one index-and-pane pattern for six screens** (P3, P6; answers
B-F3, B-F4, B-F5):

- **Above the list:** a search box and a status filter with counts. Each screen
  opens on the status its job needs: Orders on *To ship* (Confirmed), Returns on
  *Open*, Products on *All*. A dashboard figure opens the list with its filter
  already set (P9).
- **The row keeps its one primary action inline**, the way the shortest path runs
  today: *Ship* on a confirmed order, *Publish* on a draft, *Approve* on a return.
  Clicking anywhere else on the row opens the **pane**. At 1024 px and wider the
  pane opens beside the list and keeps your place in it. Below that it opens as a
  full sheet with *Back*.
- **The pane shows everything the decision needs**: the product with its
  images, both languages and what is missing; the order with its lines, payment,
  recipient and history; the claim with its quotes. It holds the screen's one clay
  action.
- **The destructive action is set apart and asks** (B-F5, P12). *Cancel order*
  lives at the foot of the pane as a quiet danger button, never beside *Ship*. It
  opens a confirmation that says what follows: "Cancelling releases the payment
  hold. It cannot be undone." A reversible action does not ask. It offers *Undo*
  instead, which is the domain's own reverse: *Suspend* offers *Reinstate*, and
  *Withdraw* offers *Publish*.
- **On a phone the table becomes cards.** Each card shows id, name, total, status
  and age, with the primary action on the card itself, so there is no sideways
  scroll (B-F4). A table that has to stay a table, such as the audit, scrolls
  inside its own frame with the cue the other tables use (B-F2).
- **Where it applies:** Orders, Returns, the Review queue, Claims, Products and
  Agents. **Products gains *Withdraw*** (the slice built 2026-09-27) in its pane,
  as a secondary action with *Undo*.

**Bulk shipping** (P4, conditional) is left out of R-4: at eight confirmed orders
a checkbox column costs more attention than it saves. It is written down, with
the number (the confirmed orders per day) that would bring it back.

**The command palette** (P2, with P11). *Ctrl K*, or `/`, opens a
`<dialog>` holding a combobox (the WAI-ARIA APG pattern). It finds three kinds of
thing:
- screens, by name in either language;
- the current screen's actions;
- an order by the first characters of its id, a product by code or name, an agent
  by name.

Enter goes to the thing, with its pane open. It needs two small server changes:
`ListOrders` gains `status` and `q`, and `ListProducts` gains `q`. The list
filters need the same two. The review queue and claims gain `j`/`k` to move
and `Enter` to open, and the palette lists those shortcuts. R-6 keeps the
shortcuts only if `via: shortcut` shows they are used.

**Home** (P9, P10). Every figure is a link to the filtered list behind it:
*3 drafts* opens the review queue, *8 to ship* opens Orders on *To ship*. The
operations widget sets its figures against targets, which live in
`DashboardFigures` beside the figures, on the server:
- **cancellations**: 2.5 % or less;
- **late shipments**: 4 % or less.

Both are Seller Central's published account-health targets, borrowed until the
shop has its own. Each figure shows as *within*, *near* or *outside* its target,
as a word and a colour.

*Built in R-4.5 for cancellations only.* The late-shipment rate needs the
moment each order shipped, a status history the shop does not keep, so its
target waits for that history rather than being computed from something else.

**The rest, in passing:**
- **Skeletons shaped like the content** replace *Loading…* (P8, B-F10). Every
  list and pane reserves its size, so nothing jumps (CLS).
- **Copy in the shopkeeper's words** (B-F6). "The state machine decides which move
  is legal" is removed rather than translated; the buttons already say it.
  "Audit 13 refused" becomes *Audit* with a *13 refused* filter.
  `CompleteCheckoutSessionCommand` becomes *Checkout completed*.
- **The empty review queue** says "Nothing waiting for review. New products appear
  here when the catalogue is imported." It has no clay button. *Import* moves to
  the Products screen's secondary actions (B-F7).
- **Targets:** 24 px minimum everywhere, and 44 px wherever the pointer is coarse
  (B-F8).

### Storefront

**The menu the header promised** (S-P1, S-P2, S-P3; answers S-F1, S-F2 and S-F6).
*TIENDA* stops being a label and becomes the button it looked like: *Tienda* /
*Shop*, a disclosure (`aria-expanded`) rather than an ARIA menu, because its
content is links.
- **On a desktop** it opens a panel below the header, on click and never on hover
  alone. The panel has one column per department: *Ropa*, *Hogar*, *Bolsas y
  mochilas*. Each column is headed by a link to the whole department, and the
  second level appears as group headings over their categories. That is 23 links
  in all, inside NNg's limit.
  - The current department is marked, as `aria-current` and not only by colour.
  - `Esc`, a click outside, or choosing a link closes it, and focus returns to the
    button.
- **On a phone** it is a drawer, a modal `<dialog>` from the left. Every
  department and category is visible under its heading, with no accordions,
  because R-0's criterion says *at most two interactions*: *Tienda*, then the
  category. An accordion would make it three. Rows are 48 px (S-P3). The language
  switch moves to the drawer's foot.
- **The phone header** becomes two rows: *Menu*, the logo on one line without its
  tagline, and the cart on the first; search at full width on the second (S-F2).

**The sticky header** (S-P4, conditional): only the first row sticks, 56 px, and
search scrolls away with the page and comes back as an icon in the sticky row.
The condition R-2 set is measured in the mock-up of the product page at 390×844:
*Añadir al carrito* has to stay inside the first screen. If it does not, the
header does not stick.

**The rest, in passing:**
- **Checkout offers the shipping options as soon as the postal code is complete**
  (S-F3). *¿Cómo quieres recibirlo?* stops being a click, and S4 drops from nine
  interactions to eight.
- *Comparar*, *Seguir comprando*, *Tu cesta* and the breadcrumb's *Tienda* grow
  to 44 px on touch (S-F4).
- **Novedades** shows whole cards with previous and next buttons, instead of
  cutting the last card in half (S-F5).

### What it should move: the hypothesis R-6 tests

| Job | Baseline (minimum interactions) | Proposed | What changes besides the count |
|---|---|---|---|
| B1 publish a draft | 2 | 2 — *Review queue* (or its badge) → *Publish*, from any screen | the pane shows what to check without leaving the list |
| B2 review the weakest claim | 2 | 2 | quotes beside the claim; `j`/`k` down the queue |
| B3 find an order and ship it | 2 + scan; plus a sideways scroll on a phone | 2 — *To ship* → *Ship* on the row; 3 with a search | no scan, no sideways scroll |
| B4 decide a return | 2–3 | 2–3 | phone: a card instead of a sideways table |
| B5 suspend an agent | 4 | 4 | *Undo* instead of a dead end |
| S1 reach a category | 1–2, and only via home | **2 from any page**, phone and desktop | the header has a way in at all |
| S2 find a product described | 3 | 3 | search stays reachable on a phone |
| S3 add a variant | 1–2 | 1–2 | — |
| S4 pay as a guest | 9 | **8** | no click to learn the shipping |

**An honest reading of it.** B1 and B2 are at their floor already, which is to go
there and act. Doing them in one interaction would mean publishing or approving
from a list without seeing the thing, and R-2 rejected that (P4, ADR 0018).

So R-0's first criterion, *fewer interactions than the baseline*, cannot hold for
B1, B2, B4 and B5 without breaking a rule the project keeps on purpose. Only its
second half, *and none takes more*, can. For those four jobs the target is
**time on task and SEQ ≥ 6**. The count's gains land where R-1 found the damage:
- B3's scan and sideways scroll;
- S1 from every page;
- S4's extra click.

This is written down now, before building, so that R-6 does not discover it and
call it a result. **It is the author's to accept.**

### Light and dark, chosen by the person (both apps)

*Asked by the author on 2026-09-27, and taken into the redesign rather than after
it: the shell is being rebuilt anyway, and a theme added later would mean
touching every new screen twice.*

- **Both themes already exist.** `tokens.css` defines the light theme and the dark
  one under `[data-theme='dark']`, and `design-contrast.spec.ts` holds every
  text token to 4.5:1 in both. What is missing is the choice:
  - the backoffice hard-codes `data-theme="dark"` in its `index.html`;
  - the storefront never sets it.
- **Three options: system, light and dark.** The default is *system*, which
  follows `prefers-color-scheme`. That changes today's defaults: a backoffice
  user whose system is light gets light. The choice is a per-browser convenience,
  kept in `localStorage` and wrapped in try/catch, like the staff switch.
- **No flash.** A few lines inline in each `index.html` set `data-theme` before
  the first paint. The Angular service owns the choice after that.
- **Where it lives:**
  - in the backoffice, the account menu, beside the language;
  - in the storefront, the drawer's foot on a phone and the footer on a desktop.
    It does not go in the header, which R-3 is emptying.
- **Held by the same tests.** The accessibility ratchet runs every route in
  both themes, and the dark storefront gets its own baseline.
- **The storefront's images are the risk.** The illustrations were painted for a
  light page. On a dark one they must sit on the card's surface, not on the page,
  and the dark storefront is drawn in the mock-ups before it is built.
- **Evidence of use:** two events, because a planned event has one kind of
  consent. `backoffice_theme_chosen {theme}` is a staff event, built with R-4.1.
  `theme_chosen {theme}` is behavioural, so it waits for consent, and arrives with
  R-5.1.
  - It answers: does anybody change it?
  - It decides: if nobody does after a month, the switch goes and *system* stays.
- **Built in** R-4.1 (the backoffice shell) and R-5.1 (the storefront header and
  drawer), each **S**.

### The mock-ups, and what drawing them found

Six screens in `design/unpublished/mockups/redesign.pen`, with PNGs beside it in
`redesign/`:
- the backoffice's orders at 1440 px (rail, list and pane) and at 390 px (cards);
- the command palette;
- the storefront's *Tienda* panel at 1440 px;
- its phone drawer;
- the product page at 390 px.

Every colour is a token variable. The one colour `tokens.css` does not have, a
**scrim** behind the drawer and the palette, is declared in `proposals.json` for
the author to accept as `--scrim`. That is the flow `design-mockups.spec.ts`
protects.

The PNGs are the `.pen` exported to HTML and rendered in Chromium, because the
design tool's own capture renders new nodes blank. The same render is how they
were checked: the export carries every node's name, so a position can be read
off the page rather than guessed.

**S-P4's condition, measured.** In the mock-up of the product page at 390×844,
*Añadir al carrito* ends at **629 px**. The layout is:
- the sticky row (56 px) and the search row (52 px);
- a one-line breadcrumb;
- a 4:3 image in place of the square one.

**The condition holds, so the first row sticks.**

Drawing it found something R-1 had missed: **on today's product page the button
already ends at about 912 px, below the first screen.** R-1's capture shows it
there, under a square image and a breadcrumb on two lines. So the redesign does
not merely avoid pushing the button down. It brings the page's one clay action
back into view, and R-6 can check that on the running app.

**What the drawing itself caught**, fixed in the mock-ups or noted for R-4:
- The first drawer grouped *Cocina* as "Cocina: menaje, cafeteras…". That hid
  five categories and broke the two-tap rule, so every category is now its own
  48 px row.
- The drawer lists departments in the desktop panel's order (N4), so the current
  department can fall below the first screen. **The drawer opens scrolled to the
  current department**, and R-4 builds it that way.
- The palette showed *Go to: Orders, Returns* for a query that was plainly an
  order id. **It lists only screens whose name matches**, and an id-shaped query
  shows orders first.
- The phone's filter chips scroll sideways inside their row with nothing to say
  so. They get the fade at the edge the existing tables already use as their
  cue.

What remains is the **walkthrough on the mock-ups**: the nine jobs, done on
paper by the author. It belongs to the author, and it comes before R-4.

**Amended 2026-09-28 by R-7.3's mock-ups:** on a desktop, *☰ Tienda* left of the
logo gives way to the logo top left and a visible row of the departments, each
opening the same panel. The author found *Tienda* and the listing's *Ocultar
filtros* read as the same kind of control. The phone keeps R-5.1's header. The
reasoning and the drawing are in [`storefront-facets.md`](storefront-facets.md),
*The mock-ups*.

### The build, in order (R-4 and R-5)

Each increment ships whole and is measured. The old screen stays reachable until
its replacement passes the accessibility ratchet and the e2e suite.

| # | Increment | Answers | Size |
|---|---|---|:-:|
| R-4.1 | Shell: side rail, groups, badges, drawer, account menu, one-row header, fluid content, the notice in the content column, the theme switch | B-F1, B-F8, B-F9, B-F11, P1 | M |
| R-4.2 | Orders on the index-and-pane pattern: filters, search, pane, cards on a phone, the confirmed *Cancel*; `ListOrders` gains `status` and `q` | B-F3, B-F4, B-F5, P3, P6 | L |
| R-4.3 | The same pattern for Returns, the review queue, claims, products (with *Withdraw* and *Undo*) and agents (*Undo* for suspend) | P3, P6, P12 | L |
| R-4.4 | Skeletons, copy and the empty queue | B-F6, B-F7, B-F10, P8 | S |
| R-4.5 | Home: figures as links, health against targets | P9, P10 | S |
| R-4.6 | Command palette and list shortcuts; `ListProducts` gains `q`; `via` on the action event | P2, P11 | M |
| R-4.7 | The audit's table in its own frame | B-F2 | S |
| R-5.1 | Storefront header, *Tienda* panel on a desktop, drawer on a phone, the sticky row if its condition holds, the theme switch and a dark storefront | S-F1, S-F2, S-F6, S-P1–4 | M |
| R-5.2 | Shipping on postal code, 44 px targets, Novedades with controls | S-F3, S-F4, S-F5 | S |
| R-5.3 | A notice beside *Añadir al carrito*: added, with a link to the cart, or not added — raised by the baseline walkthrough, not the audit | W-2 | S |

**Before R-4 starts**, per the delivery plan: the author reads this and marks
what they disagree with, the baseline walkthrough runs on today's interface, and
the nine jobs are walked through on the mock-ups. What fails on paper is
redesigned on paper.

## Non-goals

- **Not a rebrand.** The design system stays (`design/DESIGN.md`, `tokens.css`, one
  clay action per view). A change to it is versioned and recorded, not slipped in.
- **Not a population study.** The laboratory has no users to sample. The honest
  claim is "the author did these nine jobs faster and more easily". Real shopkeepers
  are the day the shop has them.
- **Not configurable dashboards.** That is P11-7's v2, in the standing backlog.

## Measures

- Tests for the new events: emitted once per view and per action, with consent
  respected; the tracking-plan test holds their attributes to the code.
- The Playwright paths are committed as specs, so R-6 runs them unchanged.
- The overflow check becomes a test at 360 px for every route.

## Evidence of use

The events above are the evidence, and each goes in the tracking plan with its
question and its decision:

- `backoffice_action_taken` answers "does the shopkeeper finish their jobs, and how
  fast?". If a job's time does not fall, its redesign goes back to R-3.
- `category_opened` answers "does anybody browse, now that they can?". If navigation
  stays under 10 % of listings reached after a month of the laboratory's traffic, the
  menu gets simpler, not bigger.

## Decisions (the author, 2026-09-27)

The three questions R-0 left open, decided by the author on 2026-09-27:

1. **Accessibility checking stays inside the licensing policy (option A).** No
   axe-core, since it is MPL-2.0 and tier 1 does not allow it. The audit script
   gains a WCAG contrast check of its own: the relative-luminance formula,
   applied to every visible text over its computed background. That, the
   script's existing checks and Playwright's ARIA snapshot run as a test.
   Keyboard paths and focus order are checked by hand. A one-off pass with axe
   outside the repository, if it is ever wanted, cross-checks this and adds no
   dependency.
2. **Backoffice telemetry rests on legitimate interest, with a notice to staff
   (option A).** Consent is a weak basis for an employee (EDPB guidelines
   05/2020, on the power imbalance). Instead:
   - the backoffice events are on by default, with a notice the shopkeeper sees
     once, es/en;
   - they carry the screen, the action and its outcome, never a person's id,
     only the role;
   - they are read in aggregate, never as a person's performance;
   - a staff member can switch them off.

   It is the design a real shop could deploy, and not legal advice. ADR 0041
   records it.
3. **The walkthrough has two participants: the author and one person who has
   never seen Tendero.** Ideally someone who has used a shop's admin (Shopify,
   WooCommerce, Seller Central). Everything around the sessions is prepared: the
   script of the nine jobs, a short consent text, and a page that times each job
   and records SEQ and SUS. Doing the jobs, and finding the second person, is
   the author's. The results are analysed against the baseline.
   **The kit:** [`redesign-walkthrough.md`](redesign-walkthrough.md) and its page,
   `tools/walkthrough/index.html` (built 2026-09-27).
