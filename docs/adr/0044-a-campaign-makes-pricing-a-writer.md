# 0044 — A campaign makes Pricing a writer

**Status**: accepted (the author, 2026-09-29, with R-8's proposal)

## Context

Pricing has read committed files since phase 3: price lists and promotions are
JSON in `seed/`, loaded at startup, and Pricing owns no table. ADR 0014 names
that as the reason Pricing was a context with no writer — "a context with no
writer is not a context yet" — and the promotion engine's purity (it references
SharedKernel and nothing else, and an architecture rule computes that by
reflection) is what lets its combination rules be proved with properties.

R-8 (the home that sells, `docs/specs/storefront-home.md`) asks for campaigns:
*Tendero Days*, *Black Friday*, a season, a week of the kitchen — named windows
over promotions. The author decided they are **set in the backoffice, for
concrete dates**: the shopkeeper creates one, names it in both languages, gives
it a window, a kind, a scope and a priority, chooses the promotions it groups,
may give it an image, schedules it and may end it early. That is a lifecycle, an
actor and a consistency boundary of its own — the three things ADR 0014 asks of
a context — and it has to be written somewhere.

## Decision

**A campaign is Pricing's first aggregate with a table.** Pricing becomes a
writer; promotions stay committed files for now.

1. **`Campaign` lives in `Pricing.Domain`**, beside the promotions it groups, and
   references SharedKernel only — the architecture rule that holds Pricing to
   that is unchanged. Its lifecycle is a `TransitionTable<CampaignStatus>`
   (invariant 5): **Draft → Scheduled → Running → Ended**, with Scheduled →
   Draft (unscheduled to edit) and Draft/Scheduled → Ended (called off). Running
   is not a state anybody sets: a scheduled campaign runs when its window opens
   and ends when it closes, so the *stored* status is Draft, Scheduled or Ended,
   and **Running is computed from the window at read time** — the rule R-7.3
   wrote down for *on offer*: a fact that time alone changes is computed when
   read, never stored.
2. **A campaign references promotions by code, as values** (ADR 0014): it never
   holds a `Promotion`. Codes are checked against the promotions Pricing knows
   when the campaign is saved.
3. **The port is Pricing's; the adapter is Persistence's.** `ICampaignRepository`
   in `Pricing.Ports`, `EfCampaignRepository` in `Persistence`, as Knowledge's
   claims and Accounts' agents already work. The engine never sees a campaign:
   it keeps choosing between promotions by their own combination rules, and a
   campaign only decides what the shop leads with.
4. **Every change is a command with an audit row** (who, what, when, allowed or
   denied), as every backoffice change is.
5. **The image is stored and served as product images are**, by id, with alt
   text in both languages (invariant 6).

## Consequences

- Pricing gains a migration and a table (`pricing_campaigns`); the promotion
  engine and its property tests are untouched.
- The day promotions move into the database too, they follow this pattern; this
  ADR does not decide that.
- ADR 0014's sentence about Pricing is now history: Pricing has a writer. The
  context count does not change.
- A campaign's status on the wire is a closed set (ADR 0043): `Draft`,
  `Scheduled`, `Running`, `Ended` — the last two computed or stored as above.
