# ADR 0066 — Imported history is born closed, through a named entry a table declares

**Status**: accepted (2026-10-24, phase 16, S2b, `P16-18`; spec `docs/specs/shopify-leave.md`, decision 4 and S2b-2)

## Context

Invariant 5 says a state machine is a table: `Order.AllowedTransitions` is the single
source of truth, and no status changes outside `TransitionTo`. An order is placed in
`Pending` and walks to `Delivered` or `Cancelled` one edge at a time, each edge raising
the event its consumers act on: `OrderPlaced` reserves stock, `OrderConfirmed`
captures the payment, `OrderShipped` and `OrderDelivered` open the returns window.

A merchant leaving Shopify brings years of orders that **already happened**. Walking
each one through the machine would reserve stock that was sold long ago, capture
money that was captured then, and raise events whose consumers send things. Writing
`Status = Delivered` past the table would be the bug the invariant exists to prevent,
done on purpose and invisible to the next reader. A refund is the same problem one
aggregate over: Tendero's refund is a `ReturnRequest`, with a table of its own.

## Decision

**A table declares where its aggregate may be born** (`TransitionTable.Entries`), as it
declares the edges, beside them. The order's says `Pending`, for an order placed, and
`Delivered` and `Cancelled`, for an order imported. A factory that creates the
aggregate in a state the table does not list is refused by the same guard
(`EnsureEntry`), and the domain model's diagram draws each entry as `[*] --> state`,
so the exception is in the picture, not only in the code.

**One named factory per aggregate brings history**: `Order.Imported(...)` and
`ReturnRequest.Imported(...)`. Each:
- is born in its final state — an order `Delivered` or `Cancelled`, a return
  `Refunded` — and refuses any other;
- keeps every check its placing counterpart makes: lines, one currency, totals that
  add up (the order's are shared with `Place`, not copied);
- **raises no event**: nothing reserves, captures, restocks or writes to anybody;
- records where it came from (`ImportedFrom`: the source, its id and the number the
  merchant knew it by) and when it happened (the source's dates, not the import's);
- is keyed for idempotence by its source (`import:<source>:<id>`), so importing the
  same store twice brings each order once.

## Consequences

The invariant holds in a narrower, stated form: **a status changes only through
`TransitionTo`, and an aggregate is born only in a state its table lists.** Every
other entry point stays refused by construction.

The dashboard and the reports see imported orders as the shop's history, which they
are; `ImportedFrom` tells them apart from orders placed here when a figure must (an
import is not a sales day). An imported order has no quote of its own: its
`OrderQuote` names the source (`import:shopify:<id>`) rather than a quote Pricing
issued, and nothing revalidates it, because nothing will charge it.

An imported order cannot be moved: `Delivered` and `Cancelled` are terminal. A return
on a delivered imported order still opens through `ReturnRequest`, as on any other,
and an imported refund is already closed.
