# ADR 0042 — Work is done beside the list; what can be undone offers Undo, what cannot asks first

Status: proposed · 2026-09-28 · by the redesign's R-4, for the author to confirm · Related: [ADR 0010](0010-frontend-sharing-boundaries.md) · [ADR 0041](0041-staff-usage-is-measured-under-a-notice-not-consent.md) · Spec: [`redesign.md`](../specs/redesign.md) R-3, R-4

## Context

Before the redesign, every screen the shopkeeper works through item by item —
orders, returns, the review queue, claims, products, agents — was a table with
buttons on its rows, drawn its own way. On a phone the buttons were past a
sideways scroll. The destructive action sat beside the useful one, and one click
cancelled an order (R-1, B-F3 to B-F5). R-2 weighed what large admins do. R-3
chose one pattern, and R-4 built it for six screens on shared components. What
was decided there constrains every list of work to come, so it is written down.

## Decision

1. **A list of work keeps its likely action on the row, and opens an item in a
   pane beside it.** The pane sits beside the list from 1024 px and is a sheet
   over it below. It carries what the decision needs and the view's one clay
   action. The filter, the search and the open item live in the URL, so a
   dashboard figure or the palette can open the list exactly there.
2. **The parts are shared, and a new list uses them** (`frontend/apps/backoffice/src/app/work/`):
   - `WorkPane` owns the focus, `Esc` and the landmark;
   - `FilterChips` gives a filter with its count;
   - `ConfirmDialog` asks before what cannot be undone;
   - `Undo` offers the reverse for what can be.

   A screen that needs something they do not do extends them rather than
   drawing its own.
3. **What the domain can reverse, and has not left the shop, offers Undo and
   does not ask.** Undo is the reverse operation on the server, audited like any
   other change: reinstating a suspended agent, publishing a withdrawn product.
   It is never a local rollback.
4. **What cannot be reversed asks first, set apart from the likely action.**
   That covers cancelling an order and rejecting a return. The question says
   what will follow, and it can ask why when the domain keeps a reason.
5. **No optimistic state changes.** A button waits and then shows the server's
   answer in its own words (ADR 0010). A state the server has not confirmed is
   never painted.
6. **On a phone a list is cards**, with the action on the card. The one table
   that must stay a table scrolls inside its own frame, with the edge fade as
   its cue.
7. **The theme is the person's, in both apps**: system by default, light or dark
   by choice, set before the first paint, kept per browser. The tokens hold both
   themes and the contrast test holds every text in both.
8. **Every new affordance is measured** before it is kept:
   - `via` (row, pane or page) on `backoffice_action_taken`;
   - `backoffice_affordance_used` for the palette and the list keys;
   - `backoffice_theme_chosen` and `theme_chosen` for the theme.

   R-6 removes what nobody uses.

## Consequences

- A seventh list costs a template and its own columns. What makes it work well
  — focus, Esc, counts, confirmations, Undo — is already written and tested.
- Bulk actions are not part of the pattern. R-2 rejected them for judgements
  (claims, publishing), and bulk shipping waits in the standing backlog for the
  number that would justify it.
- The accessibility ratchet runs each app in its default theme only. Running
  every route in both themes, as R-3 intends, needs a baseline per theme. That is
  the next step for the ratchet, not yet done.
