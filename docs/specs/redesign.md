# Spec: the redesign, run as a lifecycle — R-0, define before designing

**Status**: draft (R-0 written 2026-09-27; the baseline waits for a measurement run)
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

## R-2 — benchmark: what the good ones do, judged against R-0

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
