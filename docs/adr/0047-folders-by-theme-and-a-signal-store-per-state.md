# 0047 — Folders by theme, and a SignalStore for every state with a life of its own

**Status**: accepted (the author, 2026-09-29, while R-9's Plan 2 closed)

## Context

R-9's Plan 2 left the dashboard as thirteen widgets, each a component reading
its own data as signals, with the rules they share in two places: the reads
(`DashboardSources`, one request per endpoint, period and instant) and the clock
(`DashboardRefresh`). It worked, and the whole-branch review found the kind of
bug the shape invites: every widget re-implemented how it asks, so the minute's
refresh blanked all thirteen in the same way, and the fix had to reach thirteen
files. The feature folder itself had grown to some seventy files side by side.

The author asked for three things: that each widget live in a folder of its
own; that each have its own signal store — **every** widget, not only those
with logic, because a pattern that applies "where it is needed" is a pattern
each reader has to re-decide; and that this be the convention of the whole
backoffice and the storefront, not the dashboard's only.

**How to write a store** was weighed with the author:

1. **Our own**: an `@Injectable` per component holding its resources and
   computed signals. No dependency; every store invents its own shape.
2. **NgRx SignalStore** (`@ngrx/signals` 22.0.1, MIT, **tier 1**: it ships in
   the backoffice; one dependency of its own, `tslib`, already ours; peers
   Angular ^22). A known shape — state, computed, methods, props, and
   features shared between stores — documented outside this repository.

The author chose NgRx SignalStore: a shape a reader already knows is worth one
dependency, and it is material for the book's comparison of the two.

**Where the folders go** took two attempts the same day. The first put the
widgets under `widgets/`; the author asked for `components/`, so that every
feature would read the same, and it was built. Then the author asked what
Angular 22 recommends, and its style guide (angular.dev/style-guide, *Project
structure*) says the opposite of `components/`: organise by feature areas and
common themes, not by the type of code — it names `components/` and
`services/` as the folders to avoid — split a folder that grows too large into
subfolders by theme, keep a component's files and its tests together, and name
files for what they hold, never `helpers` or `utils`. The author chose the
guide.

## Decision

1. **Folders by feature and theme, never by type of code.** A feature keeps at
   its root its page (or pages), its routes and, when its parts share one
   state, its page store. A component that is a concept of its own lives in a
   folder named for it; when a feature has many of one kind, they gather under
   a folder named for that theme. `components/`, `services/`, `helpers/`,
   `utils/` and a separate `tests/` are not used. Tests sit beside their code.

   ```
   features/dashboard/                       features/orders/
     dashboard-page.*  dashboard-routes.ts     orders-page.*
     dashboard-layouts.ts  …                   orders.store.ts
     grid/     dashboard-grid.*  widget-outlet.ts
     widgets/  key-figures-widget/             order-list/   order-list.*
               sales-by-day-widget/  …          order-pane/   order-pane.*
               figures.ts  widget-testing.ts
     sources/  dashboard-sources.ts  dashboard-refresh.ts  with-dashboard-period.ts
   ```

   `widgets/` is not a type of code here: it is the dashboard's theme, the
   pieces its grid places by key. `sources/` holds the reads the widgets share,
   `grid/` the grid and the outlet it draws each widget through.
2. **A SignalStore for every state with a life of its own — the store follows
   the state, not the component.** The question is who else reads or changes
   it:

   | The state is… | Its store is… | Here |
   |---|---|---|
   | used by one component alone | that component's: `<name>.store.ts` beside it, provided by it | each dashboard widget, even the one that only shows a figure; the orders pane's detail (`order-pane.store.ts`) |
   | shared by several components | one, in their closest common parent, which they inject | Orders' page store: the open order, the rows, "moving…", the refusals and the moves, which the list and the pane both read and change |

   Shared state is never split into two stores kept in step — two copies of one
   fact disagree sooner or later. A child store may inject its parent's (the
   pane's reads the page's), never the reverse: the page's store knows nothing
   of the pane, and the pane sees its order change and asks for its detail
   again. The component presents (formatting in the reader's culture included);
   the store reads and derives.
3. **A `signalStoreFeature` for what more than one store needs, or to split one
   that grew.** Use one when the same state and behaviour appear in several
   stores — `withDashboardPeriod()` gives the period, follows the widget's
   input (`signalMethod`) and keeps a read idle until there is one, in eleven
   widget stores; the lists of work (orders, returns, stock, catalogue,
   claims…) will share their filters, search and page in the URL the same way —
   or when one store has grown several responsibilities and reads better as
   parts composed into it. Not to tidy a store used once. A feature lives beside
   the stores that share it, or in `worklist/` when they are in different
   features.
4. **What a store is not for.** A component with no state of its own gets no
   store: sign-in, the shell, the nav, a status label. Input that lives only
   while it is typed — the reason in a confirm dialog — stays a component
   signal. Inside a store, `patchState` changes our own state, a resource holds
   what the server is read for (`readStatus` turns it into loading and
   failed), and `HttpClient` in a method sends what the server is asked to
   change, reading the resource again after it answers.
5. **It is the convention for the whole backoffice, and next for the
   storefront.** Outside `features/`, `shell/` is the frame round every screen
   (nav, account menu, palette, the staff notice) and `worklist/` what the
   lists of work share (the pane, the filter chips, the pager, the confirm
   dialog, Undo). The dashboard and Orders were the reference; every other
   feature followed screen by screen, each with its store specs and a hand
   mutation. The storefront followed in a pass of its own: the state every
   page shares lives in root theme folders (`cart/`, `compare/`, `taxonomy/`,
   `returns/`, `campaigns/`, `trail/`, `listing/`), `data-access/` keeps HTTP
   services only, and each page provides its store — a listing page provides
   two, `ListingStore` and its own, which tells the listing what to list. A
   live object (the listing the browser agent reads) is held in a signal of its
   own, never in a store's state, which is frozen.

## Consequences

- One dependency more, tier 1, pinned exactly (`22.0.1`); it follows Angular's
  major, so it moves with every Angular upgrade.
- Stores are tested without a DOM (`TestBed.inject(Store)` with the providers
  its component has), and the component specs keep testing what the reader sees.
- R-10's live source joins a widget in its store, not in its component.
- Both frontends follow it. The initial bundles grew by what `@ngrx/signals`
  weighs once it is in main: 3.2 kB in the backoffice, 1.7 kB in the storefront.
