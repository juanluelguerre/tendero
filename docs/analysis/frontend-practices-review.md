# The frontend against its own guides — a review, 2026-10-01

**Why**: the author asked, at the end of `refactor/tailwind-utility-first`, whether
what the branch does — and what the frontend does in general — is what Angular,
NgRx and Tailwind actually recommend, "honestly, and without thinking about the
cost": the repository teaches, so a pattern it gets wrong is a pattern a reader
learns wrong. This file is the answer and the decision table that follows from it.

**How**: three read-only audits, one per guide, each against the guide's current
text and a sample of the code, then every claim that would change a rule checked
by hand against the installed packages before it was written here. Two examples
of why the check matters: one audit counted 0 uses of `dark:` — true, but the
grep that confirms it also finds two `dark:` that are object keys, not variants;
another said OnPush is now Angular's default — `@angular/core` 22.0.6's own
types say so (`NOTE: OnPush is enabled by default`), and that one is real.

**Versions**: Angular 22.0.6, `@ngrx/signals` 22.0.1, Tailwind 4.3, Prettier 3.8
with `prettier-plugin-tailwindcss` 0.8.1.

**Verdicts**: ✅ aligned · ⚠️ deviates for a reason worth stating · ❌ should change.

---

## The short answer

The frontend's architecture is better than the guides ask for in several places
and behind them in a few, and the few are specific and cheap to name:

- **Better than the guides**: boundaries enforced by lint (Nx module rules,
  `HttpClient` banned where the agent's tools live), a theme that is the design
  tokens and nothing else with a test that holds it, accessibility built in
  (captions, scopes, focus return, 24 live regions), state ownership written as
  a rule (who owns a store, who may inject whose), the URL as the source of a
  list's filters and page, `inject()` and signal APIs everywhere, no `NgModule`,
  no `@HostBinding`, no `ngClass`.
- **Behind them**: the APIs that arrived in the last two Angular versions and in
  NgRx 22 — `OnPush` by default, fetch by default, `NgOptimizedImage`, Signal
  Forms, NgRx's resource extensions, `withLinkedState`, `rxMethod` for
  concurrency — and one thing the Tailwind migration itself introduced: 275
  arbitrary lengths where the token scale is missing steps.

None of it is a bug the shopper sees today, except arguably the images (no
reserved space, every image eager) and the cart's unserialised writes.

---

## Angular 22

| # | Guidance (source) | The code | Verdict |
|---|---|---|---|
| A1 | `withFetch()` is deprecated: "`FetchBackend` is the default" (`@angular/common/http` 22.0.6, its own `@deprecated`) | both `app.config.ts` still pass it | ❌ two lines |
| A2 | "Always mark the LCP image as `priority`"; lazy by default; set width/height ([image optimization](https://angular.dev/guide/image-optimization)) | no `NgOptimizedImage` anywhere; `ProductImage` defaults to `eager`; no dimensions, so the page shifts as images arrive | ❌ measurable (LCP, CLS) |
| A3 | Without zone.js TestBed is zoneless; uninstall zone.js ([zoneless](https://angular.dev/guide/zoneless)) | four libs' `test-setup.ts` say `zoneless: false` while no zone.js loads; `zone.js` 0.16.2 still in `package.json`; libs on Analog's Vitest runner, apps on `@angular/build:unit-test` | ❌ a setting that says the opposite of what runs |
| A4 | OnPush is the default in v22 (`ChangeDetectionStrategy`'s own doc) | `changeDetection: OnPush` written on 117 components | ⚠️ was right until v21; now noise — or a statement, if the book wants one |
| A5 | `httpResource` for HTTP reads ([resource](https://angular.dev/guide/signals/resource)) | 63 `rxResource` over `data-access/` services, 0 `httpResource` | ⚠️ sound: the services are shared with the agent's tools; say it in ADR 0047 |
| A6 | Signal Forms, stable in v22, best "in new applications built with signals" ([overview](https://angular.dev/guide/forms/signals/overview)) | 22 `ngModel` (checkout, order, variants) | ⚠️ the signal model stops at the form |
| A7 | Template logic into `computed` ([style guide](https://angular.dev/style-guide)) | formatting by method calls (`money()`, `placed()`, `price()`) | ⚠️ the price and date pipes ADR 0049 names would end it |
| A8 | `host` property over `@HostBinding` ([host elements](https://angular.dev/guide/components/host-elements)) | `host: { class }`, `host: { role }`, no decorators | ✅ |
| A9 | `inject()`, `protected` template members, signal inputs/outputs, built-in control flow, `@defer`, lazy routes, no `.component` suffix, specs beside code, folders by feature ([style guide](https://angular.dev/style-guide)) | all of it | ✅ |
| A10 | Official i18n is `@angular/localize` | Transloco, switching at runtime under `/es` and `/en` | ⚠️ right for a runtime language in the URL; no ADR says so |

## NgRx SignalStore (54 stores, 1 feature)

| # | Guidance (ngrx.io/guide/signals/…) | The code | Verdict |
|---|---|---|---|
| N1 | `withLinkedState` puts a linked slice in state, under `patchState` and protection ([linked-state](https://ngrx.io/guide/signals/signal-store/linked-state)) | six stores expose a public writable `linkedSignal` (`orders.store.ts`, catalogue, stock, returns, review-queue) any component can `.set()`; `campaign-editor.store.ts` already uses `withLinkedState` | ❌ breaks state protection, and two idioms for one thing |
| N2 | `signalMethod` to feed a signal into a method, not an effect ([signal-method](https://ngrx.io/guide/signals/signal-method)) | `search.store` and `category.store` write into `ListingStore` from an `effect`; `product.store` resets the selection in one | ❌ write-in-effect |
| N3 | `rxMethod` with `switchMap`/`concatMap` for request streams ([rxjs-integration](https://ngrx.io/guide/signals/rxjs-integration)) | `ListingStore` keeps a bag of `Subscription`s by hand and has no spec of its own | ❌ the subtlest store, the browser agent's surface |
| N4 | the same, for writes that must not overtake each other | `CartStore` writes with `firstValueFrom`, unserialised; the product card, product page and agent tool can overlap; it also calls `HttpClient` directly | ⚠️ a late answer can overwrite a newer cart |
| N5 | shared behaviour in a `signalStoreFeature` ([custom features](https://ngrx.io/guide/signals/signal-store/custom-store-features)) | page size, page from the URL, `navigate(…merge)` and `go(page)` copied in five worklist stores (orders, catalogue, stock, claims, audit); orders' copy skips `readStatus`, so an expired session reads as a failure there only | ❌ by the project's own ADR 0047 |
| N6 | `@ngrx/signals/resource`: `withValueOnError`, `withPreviousValueOnLoading` (experimental) | a hand-written `valueOf` at every read, and "keep the old value while loading" rebuilt with `keptCounts` | ⚠️ defensible while experimental; `valueOf` should name the API that replaces it |
| N7 | derived values in `withComputed` | `product.store`'s `notice()` and `remaining()` are methods with no arguments | ⚠️ trivial |
| N8 | `_` privacy, `withProps` for dependencies, `withHooks`, local vs global providers, `TestBed` specs that fake dependencies | all consistent | ✅ |
| N9 | — | 8 stores without a spec (listing, active-listing, 6 widget stores), which CLAUDE.md requires | ❌ by the project's own rule |

Two of the project's rules go **further** than NgRx and should say so where the
book quotes them: *every widget gets a store* is a consistency choice of ours,
not NgRx's; *`HttpClient` in a method for writes* is weaker than NgRx's own
examples, which use `rxMethod` precisely for the race in N4.

There is no official `withResource`: it and `withMutations` are the community
`@angular-architects/ngrx-toolkit`. A resource in `withProps`, as here, is the
official-API way.

## Tailwind 4 (this branch)

| # | Guidance (tailwindcss.com/docs/…) | The code | Verdict |
|---|---|---|---|
| T1 | `--*: initial` for a fully custom theme; `inline` for variables that reference variables ([theme](https://tailwindcss.com/docs/theme)) | the theme is the tokens; `reference` is real but undocumented (tailwindcss PR 14095) | ✅ teach it, with that caveat |
| T2 | arbitrary values are for the occasional pixel-perfect break-out ([adding custom styles](https://tailwindcss.com/docs/adding-custom-styles)) | **275 arbitrary lengths**: `[2.75rem]` ×70 (the 44 px tap target, 26 of them as `pointer-coarse:min-h-[2.75rem]`), `[2.5rem]`, `[2.25rem]`, `[1.5rem]`, `[2px]`, and icon sizes in px and rem for the same thing | ❌ the scale is missing named steps — ADR 0049's "a utility can only name a token" is true for colours and false for sizes |
| T3 | a token with no theme mapping is a missing mapping | 12 colours as `bg-(--danger-tint)` and the like | ⚠️ map them; `duration-(--dur-fast)` is fine (no duration namespace) |
| T4 | a pattern reused across files becomes a component ([managing duplication](https://tailwindcss.com/docs/styling-with-utility-classes#managing-duplication)) | the sticky summary aside, repeated in cart, checkout and order | ❌ ADR 0049 point 4, missed |
| T5 | primitives in `@layer components`, no `@apply` | `.button`, `.field`, `.table`, `.tag`… | ✅; `.muted` duplicates `text-muted`, `.page-frame` keeps a raw `max-width: 640px` |
| T6 | — | `copy-text.css` (76 lines) never migrated, hand-rolls `sr-only` | ❌ contradicts ADR 0049's own list |
| T7 | unlayered CSS beats every layered utility | 19 of 21 remaining component stylesheets are unlayered | ⚠️ known for `:host`, unstated for the rest |
| T8 | `host: { class }` (Spartan/ng does it); scanning TS is supported with `@source not` for specs ([detecting classes](https://tailwindcss.com/docs/detecting-classes-in-source-files)) | a hand-kept safelist of 23 utilities held by a regex spec | ⚠️ works and is tested; scanning `*.ts` minus specs would remove the list — the stray-utility reason that ruled it out mostly went with `--*: initial` |
| T9 | dark mode by a custom variant: `&:where([data-theme=dark], [data-theme=dark] *)` ([dark mode](https://tailwindcss.com/docs/dark-mode)) | the variant is declared and **never used** — dark mode is the tokens' override, the better design | ❌ dead code; delete it |
| T10 | the Angular guide's PostCSS setup; `source(none)` + `@source`; Prettier's `tailwindStylesheet` for v4 | as recommended | ✅ |

---

## This branch, honestly

**What it did right, and is worth teaching**: a theme that is the tokens and a
dark mode that stays in them; Tailwind scanning templates only, and why; cascade
layers and the bug they caused (a layered rule losing to an unlayered container
query); primitives as classes, structure as components; every step checked
against screenshots taken before it; the host's fixed look in `host: { class }`,
empty stylesheets deleted, and a test that keeps both true; Prettier as a gate,
with the two places a line break is a visible space kept by hand; the honest
*As built* numbers in ADR 0049.

**What it got wrong**: it moved 6,000 lines of CSS into templates without first
asking whether every value it moved had a name. The 275 arbitrary lengths were
mostly literal values in the old CSS; the migration carried them across instead
of turning them into tokens, which is the one step that would have made the
theme's promise true for sizes. It also left `copy-text` behind, the summary
aside copied three times, and a dark variant nobody uses.

**What it found that predates it**: a redirect loop that froze the tab on any
unknown address inside a language (fixed on the branch, `app.routes.spec.ts`).

---

## The decision table

*Quality* is what a reader or a shopper gets; *teaching* is what the book gains
by having it right. Cost is files and risk, not hours.

| # | Change | Recommended by | Cost | Quality | Teaching | Proposal |
|---|---|---|---|---|---|---|
| A1 | drop `withFetch()` | Angular 22 | 2 lines | correctness | low | **now** |
| A3 | zoneless test setup, remove zone.js, one Vitest runner | Angular | ~6 files | the setup stops lying | medium | **now** (runner: next) |
| T9 | delete the unused dark variant | Tailwind | 1 line | less dead code | low | **now** |
| T2 | size tokens (tap target, control and icon sizes, the 2 px step) and a `tap-target` utility | Tailwind | ~200 class edits, mechanical, screenshot-checked | the theme's promise holds for sizes; one place for 44 px | **high** — the chapter's own claim | **this branch, before merge** |
| T3 | map the 12 colour tokens | Tailwind | ~12 edits | consistency | medium | **this branch** |
| T6 | migrate `copy-text` | ADR 0049 | 1 component | consistency | low | **this branch** |
| T4 | the summary aside as a component | Tailwind, ADR 0049 | 1 component, 3 pages | DRY | medium | **this branch** |
| T8 | scan `*.ts` minus specs instead of the safelist | Tailwind | theme.css, half a spec | one less list to keep | medium | **decide**: either is defensible; the safelist is more explicit |
| N1 | `withLinkedState` in six stores | NgRx | 6 stores | state protection | **high** | next branch |
| N2 | `signalMethod` / `withLinkedState` instead of effects | NgRx | 3 stores | no write-in-effect | **high** | next branch |
| N5 | `withWorklistUrl()` feature | NgRx, ADR 0047 | 5 stores | DRY, and one bug gone | **high** | next branch |
| N3 | `ListingStore` on `rxMethod`, with a spec | NgRx | 1 store, subtle | correctness under concurrency | high | next branch |
| N4 | `CartStore` writes serialised (`rxMethod` + `concatMap`), through `data-access/` | NgRx | 1 store | no lost update | high | next branch |
| N9 | the 8 missing store specs | CLAUDE.md | 8 specs | safety for N1–N5 | medium | next branch, **first** |
| A2 | `NgOptimizedImage`: dimensions, lazy by default, priority on the LCP image | Angular | 1 component + ~9 `<img>` | LCP and CLS | high | next branch |
| A4 | stop writing `OnPush` | Angular 22 | 117 files, no risk | less noise | medium | **decide**: remove, or keep as a statement and say why |
| A7 | price and date pipes instead of formatting methods | Angular, ADR 0049 | ~20 templates | no work per check | medium | next branch |
| N6 | NgRx resource extensions instead of `valueOf` | NgRx (experimental) | 2 providers + 63 resources | one less helper | high | **decide**: now, or when stable — record it in an ADR either way |
| A6 | Signal Forms for checkout, order, variants | Angular 22 | 3 pages, validation risk | one model end to end | **high** | its own phase |
| A5, A10 | state why `rxResource` over services and why Transloco | — | two paragraphs | the reasons on record | medium | **now**, in ADR 0047 / a new ADR |

The order follows one principle, the 2026-09-29 one: tests first where a refactor
has none (N9 before N1–N5), the cheap and objectively correct at once, and the
rest one reference first, then the others.

---

## Decided, 2026-10-01

The author: **all of it on this branch**, before its merge. On the three open
points: **A** — follow Angular, and stop writing `OnPush`, which is the default;
**B** — the safelist for now, with what the community does on record below;
**C** — NgRx's resource extensions instead of `valueOf`, although still
`@experimental` in 22.0.1 (the latest), recorded in an ADR. Beyond the table:
every frontend dependency updated; **all** 275 arbitrary lengths into tokens;
`NgOptimizedImage` and Signal Forms wherever they fit; `withLinkedState`,
`signalMethod` and `rxMethod` wherever they are the right tool. What each step
became is recorded as it lands, in the commits and in the ADRs they name.
