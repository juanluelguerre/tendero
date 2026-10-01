# 0051 — Angular 22 and NgRx 22, as their own guides say

**Status**: accepted (the author's decisions of 2026-10-01, after the review in
`docs/analysis/frontend-practices-review.md`); built on
`refactor/tailwind-utility-first`.

## Context

The review of 2026-10-01 read the frontend against the current text of
Angular's and NgRx's guides and found it ahead of them in architecture and
behind them in the APIs of the last two releases. The author's instruction:
"what Angular recommends", the latest versions of everything, and NgRx's own
tools wherever they are the right one — honestly, and without weighing the cost.
This records each decision and why, so the book can quote the reason and not
only the code.

## Decisions

1. **Change detection is not written.** Angular 22 makes OnPush the default
   (`ChangeDetectionStrategy`'s own documentation), so
   `changeDetection: OnPush` on 117 components had become noise. Behaviour is
   unchanged; a component that needs eager checking would say `Eager`.
2. **Fetch is not written either.** `withFetch()` is deprecated in 22: fetch is
   the default backend.
3. **Zoneless is not pretended.** zone.js is gone from the dependencies, and the
   test setups no longer say `zoneless: false` while running zoneless.
4. **A failed resource reads as no value — NgRx's resource extensions.**
   Reading `value()` of a resource in its error state throws in Angular 22, and
   so does every `computed` over it. The project first answered that with its
   own `valueOf` at 76 read sites. It now wraps every resource in
   `extendResource(...)` and provides `withValueOnError(undefined)` once per app
   and per test runner (`provideReadableResources()`, shared-util; the apps'
   `test-providers.ts` through the unit-test builder's `providersFile`), so a
   store reads `list.value()`. **These extensions are marked `@experimental` in
   @ngrx/signals 22.0.1, the latest release**, and that was weighed: the API is
   small, it is NgRx's, and if it changes the compiler will say where. The
   project's `keptCounts` stays — it keeps a list's counts across a reload *and*
   a failure, which `withPreviousValueOnLoading` alone does not.
5. **Reads stay `rxResource` over the `data-access/` services**, not
   `httpResource`, which Angular's resource guide points to for HTTP. The
   services are the port the agent tools share (an ESLint rule keeps
   `HttpClient` out of them), and a second way to reach the API would be two.
6. **Transloco, not `@angular/localize`.** Angular's i18n is build-time, one
   bundle per locale. Tendero switches language at runtime under `/es` and
   `/en` (ADR 0013), with one bundle — what Transloco is for.
7. **Vite resolves the workspace's paths itself** (`resolve.tsconfigPaths`,
   Vite 8): the `vite-tsconfig-paths` plugin, and Nx's deprecated
   `nxViteTsPaths` before it, are gone.
8. **A store never has an `effect`.** NgRx's own tools, each for its job:
   `withLinkedState` for state another signal resets (orders' rows and open
   order, the catalogue's products, stock's rows, returns' requests, the
   review queue's drafts, the product's selection); `signalMethod` to feed a
   signal into a method (the order pane's reload, the trail's account, the
   dashboard's refresh); `rxMethod` for streams of requests — `switchMap` in
   the listing, where only the latest question's answer may show, and
   `concatMap` in the cart, where a change must not overtake the last. NgRx
   protects linked state **by type**, not at runtime, so the orders spec holds
   it with a type test (`@ts-expect-error` on `store.orders.set`). A component
   keeps `effect` for what is outside signals: the DOM, the document's head,
   the router, ECharts, a timer — what Angular keeps it for.
9. **What the lists of work share is a store feature**, `withWorklistUrl()`:
   the query, the page, `_navigate`, `go`, one page size on the store. A
   second feature typed on the store's `total`, to clamp `go` to the last page,
   was written and set aside: NgRx's overloads cannot infer a feature with a
   typed input after a custom feature (`signalStore(custom(), …, typed())` does
   not compile; with a plain `withProps` first it does). The clamp went where
   every list already is: the pager moves from the page it shows.
10. **Every call goes through `data-access/`.** The cart was the one store
    with `HttpClient` in it; it asks `CartService` now.
11. **Formatting in a template is a pure pipe** — `price`, `dateTime`, `day`,
    `clock` in shared-ui, over the formatters in shared-util — with the
    language as an argument, so a switch reformats. A formatter a library calls
    (a chart's axis) or a sentence built in TypeScript stays a function.
12. **Pictures are `NgOptimizedImage`**, `priority` on each page's Largest
    Contentful Paint. **No loader and no `srcset` yet**: `/api/images/{id}`
    serves one size, and image derivatives are deferred by the scale rule
    (CLAUDE.md). The day the endpoint takes a width, a loader is one provider.
13. **Forms are Signal Forms** (`form()`, stable in 22). A form writes its
    model, and a SignalStore's state is frozen, so the model is a writable
    signal in the store's props when a store owns the form; the schema says
    what is required and allowed, and the `[formField]` directive owns `name`,
    `required`, `min`/`max` and `maxlength` on the input. Its names are
    `<form>.<field>`, so a form a browser suite or autofill reads is named
    (`address.recipientName`).

## Consequences

- Stores read `x.value()`, as Angular's and NgRx's documentation write it; a
  reader who knows either recognises the code.
- An experimental API is on the critical path of every read. If NgRx changes
  it, the change is in one provider and the `extendResource` calls the compiler
  points at; if NgRx drops it, `provideReadableResources()` is the one place a
  replacement goes.
- Every one of these was held, as it landed, by a spec written or changed
  first and a hand mutation that turned it red; the commits name each.
- The test for it states both halves: with the provider a failed resource
  reads as nothing, and without it Angular 22 throws — so the day Angular stops
  throwing, that test says the provider can go.
