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
   - `backoffice_view_opened {view}`: the shopkeeper opened a screen.
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

## Open questions (the author's to decide)

1. **An accessibility checker.** The usual tool, axe-core, is **MPL-2.0**, which the
   licensing policy's tier 1 (MIT, Apache-2.0, BSD) does not allow as a dependency.
   Lighthouse (Apache-2.0) bundles axe-core. Choices:
   - by hand against WCAG 2.2 AA, with Playwright's accessibility snapshot, which
     needs no new dependency;
   - an exception recorded as an ADR, for a development-only dependency that is
     never redistributed;
   - running axe outside the repository.

   *Recommendation*: by hand for R-1, with the snapshot. It is what a heuristic
   audit is anyway, and it adds nothing to the dependency budget.
2. **The legal basis for backoffice telemetry.** The shopkeeper is staff, not a
   shopper. Do their events follow the consent banner, or a staff notice?
3. **A second participant** for the walkthrough, if there is one.
