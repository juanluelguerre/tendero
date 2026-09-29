# 0047 — A widget is a folder with its SignalStore

**Status**: accepted (the author, 2026-09-29, while R-9's Plan 2 closed)

## Context

R-9's Plan 2 left the dashboard as thirteen widgets, each a component reading
its own data as signals, with the rules they share in two places: the reads
(`DashboardSources`, one request per endpoint, period and instant) and the clock
(`DashboardRefresh`). It worked, and the whole-branch review found the kind of
bug the shape invites: every widget re-implemented how it asks, so the minute's
refresh blanked all thirteen in the same way, and the fix had to reach thirteen
files.

The author asked two things of it: that each widget live in a folder of its
own, and that each have its own signal store — **every** widget, not only those
with logic, because a pattern that applies "where it is needed" is a pattern
each reader has to re-decide. And that this be the backoffice's convention, not
the dashboard's only.

Two ways to write a store were weighed with the author:

1. **Our own**: an `@Injectable` per widget holding its resources and computed
   signals, provided by the component. No dependency; every widget invents its
   own shape.
2. **NgRx SignalStore** (`@ngrx/signals` 22.0.1, MIT, **tier 1**: it ships in
   the backoffice; one dependency of its own, `tslib`, already ours; peers
   Angular ^22). A known shape — state, computed, methods, props, and
   features shared between stores — documented outside this repository.

The author chose NgRx SignalStore: a shape a reader already knows is worth one
dependency, and it is material for the book's comparison of the two.

## Decision

1. **A widget is a folder**: `features/dashboard/widgets/<name>/` holds its
   component (`<name>-widget.ts/.html/.css`), its store (`<name>.store.ts`),
   and their specs. What widgets share sits beside them: `widgets/figures.ts`
   for saying a figure, `data/` for the reads and the clock, `testing/` for the
   specs' way in.
2. **Every widget has its SignalStore**, provided by the component, so it lives
   and dies with the widget. The component presents; the store reads and
   derives. A widget that only shows a figure has a small store — the pattern
   does not change with the size.
3. **What stores share is a store feature**: `withDashboardPeriod()` gives the
   period and follows the widget's input (`signalMethod`); reads go through
   `refreshedResource` and `DashboardSources` as before.
4. **It is the convention for the rest of the backoffice and the storefront**,
   migrated in a plan of its own, screen by screen, with its before and after —
   not inside R-9.

## Consequences

- One dependency more, tier 1, pinned exactly (`22.0.1`); it follows Angular's
  major, so it moves with every Angular upgrade.
- Stores are tested without a DOM (`TestBed.inject(Store)` with the providers
  a widget has), and the widget specs keep testing what the shopkeeper reads.
- R-10's live source joins a widget in its store, not in its component.
- The rest of the application does not follow the convention yet; until its
  plan runs, the dashboard is the reference.
