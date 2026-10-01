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

## Consequences

- Stores read `x.value()`, as Angular's and NgRx's documentation write it; a
  reader who knows either recognises the code.
- An experimental API is on the critical path of every read. If NgRx changes
  it, the change is in one provider and the `extendResource` calls the compiler
  points at; if NgRx drops it, `provideReadableResources()` is the one place a
  replacement goes.
- The test for it states both halves: with the provider a failed resource
  reads as nothing, and without it Angular 22 throws — so the day Angular stops
  throwing, that test says the provider can go.
