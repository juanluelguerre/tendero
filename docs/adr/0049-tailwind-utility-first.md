# 0049 — Tailwind, utility-first, over a theme that is the design tokens

**Status**: accepted (the author chose it on 2026-09-30, after a survey of how
both apps used Tailwind); built on `refactor/tailwind-utility-first`, 2026-10-01

## Context

Tailwind 4 was brought in CSS-first (`libs/shared/tokens/src/theme.css`): an
`@theme` mapping `design/tokens.css` into Tailwind's namespaces, three custom
utilities and a dark variant. The survey of 2026-09-30 counted what the two apps
actually did with it:

- **Templates hardly used it.** Of about 2,100 class tokens in 104 templates, the
  only utilities were `sr-only` and our own `numeric` and `awning`. None of the
  colour, text, radius or font utilities the `@theme` generated appeared anywhere.
- **The styling was component CSS over tokens**: about 6,300 lines with 1,800
  direct `var(--…)` references and BEM names, no `@apply`, no `@reference`.
- **And it had drifted into traps.** Tailwind scanned the whole workspace (TS,
  specs, markdown) and emitted utilities nobody wrote; three of our class names
  were also utilities (`.table` gave every backoffice table `display: table` from
  a second source); Tailwind's default palette and scales lived beside ours under
  the same names (`stone-*`, `text-*`, `radius-*`); the `@theme` held
  self-referencing entries (`--font-display: var(--font-display)`) that resolved
  only because the unlayered tokens won the cascade; the reset was written three
  times; `.sr-only`, `.numeric` and the awning each existed twice; and the v3
  preset the bridge replaced was still in `design/`.

Three ways were weighed: **A** keep the component CSS and clean Tailwind down to
the reset and the bridge; **B** remove Tailwind; **C** move the styling into
utilities in the templates, which is how Tailwind is meant to be used.

## Decision

**C: utility-first.** A component's look is written in its template with
utilities; its stylesheet keeps only what utilities cannot say well — a chart's
internals, a complex selector, an animation — and many end up empty.

1. **The theme IS the design tokens, and only them.** `@theme inline` with
   `--*: initial`: every utility — `bg-surface`, `text-muted`, `p-4`, `rounded-md`,
   `md:` — resolves to a token of `design/tokens.css`, and Tailwind's own palette,
   spacing and breakpoints are gone. The tokens stay the one source (ADR 0033),
   dark mode stays the tokens' `[data-theme='dark']` override, and the
   no-hex test keeps its meaning: a utility can only name a token.
2. **It reads the templates and nothing else.** `@import 'tailwindcss'
   source(none)`, then `@source` over the apps' and libraries' templates.
3. **One base, in layers.** What both apps share lives once in
   `libs/shared/tokens/src/base.css`, in `@layer base` and `@layer components`, so
   a utility on an element wins over it, as Tailwind intends.
4. **What repeats becomes a component, not a string of utilities copied.** The
   inventory of 2026-09-30 (notice, status pill, price and date pipes, list
   states, icon, drawer) is built as the migration reaches each piece; a
   component, not `@apply`, is where a long class list lives once.
5. **Piece by piece, never a rewrite.** The base first, with no visible change;
   then each template, one commit at a time, each checked against screenshots
   of both apps taken before it.

## Consequences

- The three-files convention became two-or-three (amended 2026-10-01, below): a
  component's `.css` exists only when it holds something utilities cannot say.
- BEM names disappear from templates as they migrate; a class that remains is a
  hook for a test or a stylesheet, and says so.
- Templates get longer. That is the trade Tailwind makes, and why point 4 matters.
- Raw media queries become the theme's breakpoints, defined from the widths the
  CSS already used (640px, 64rem, 39.99rem), so the migration does not move them.

## As built (2026-10-01)

Eleven commits: the base, the first two shared components, then nine batches
(six storefront, three backoffice), each checked against 92 screenshots of both
apps — 1440 and 390, both themes, panes, a confirmation, the palette, the
drawers, the checkout, the account — taken before it, plus 20 to 50 extra
before/after captures of the states each batch touched, every write answered
inside the test so the shop's data never moved.

- **Component CSS**: storefront 4,038 lines to 229, backoffice 2,019 to 516
  (the widget frame's size contract, 89 lines of container queries, among
  them); global `styles.css` 294 to 215 and 676 to 422; `libs/shared/ui` 109.
- **What stays in CSS**, and is allowed to: a `:host` that depends on a state,
  a query or a context (see *The host*, below), keyframes, generated content (`::after` with `attr()`), anchor
  positioning and `@supports`, container queries (the theme defines no
  `--container-*`), and a rule a test reads from the stylesheet itself
  (`design-contrast.spec.ts` reads the wordmark's).
- **The apps' primitives stay classes** (`.button`, `.field`, `.select`,
  `.table`, `.tag`, `.panel`, `.page-head`, `.worklist-*`, `.pane-*`), in
  `@layer components`: tiny and repeated on every screen, which is where
  Tailwind's own guidance keeps a component class.
- **Shared components it produced**: `lib-notice`, `lib-status-pill`,
  `lib-icon` (the backoffice's set, moved and merged with the storefront's
  inline SVGs), `lib-culture-switch`, `lib-theme-switch`, `lib-copy-text`;
  storefront `page-state`, `totals`, `crumbs`, `product-image`; backoffice
  `list-state`, `find`, `widget-frame`. Each with a spec that failed first.
- **One `ViewEncapsulation.None`**, the repository's first: the widget frame's
  size contract has to reach elements in seventeen widgets' templates. Its
  rules sit in `@layer components`, so utilities still win.
- **What the layers broke, and what the screenshots did not see**: the wall's
  rule that hides a chart's figures table moved into a layer and lost to the
  chart's unlayered container query; the dashboard's own browser spec caught
  it a batch later, not the screenshots, which have no 1920 wall. The full
  end-to-end suites of both apps ran at the end.
- **What it found that was already broken**: two failure notices with no styles
  (their classes lived in another page's stylesheet), the audit's outcome pill
  (classes no stylesheet defined), text skeletons 0px tall, a gallery skeleton
  that did not animate, a reference whose `numeric` a later `font: inherit`
  silently overruled, a list region announced with its text already in it.
- **Accepted, and said in each commit**: `max-sm:` is `width < 40rem`, so the
  old `max-width: 640px` rules no longer apply at exactly 640px (likewise
  480, 560, 900, 1100, 1200).


## Amendment (2026-10-01): the host, and the empty stylesheets

The migration left 25 stylesheets holding only a comment and 68 holding only
`:host { display: … }` — the rule above kept them so that every component stayed
three files. The author called them noise, and they were: a reader opened a file
to learn that a component is a block.

> **⚠ Where the host's look goes** — the rule, also in `CLAUDE.md`:
>
> - **Fixed → `host: { class: '…' }`** in the decorator, as Angular's style guide
>   prefers the `host` property, and as Spartan/ng and most Angular-with-Tailwind
>   code do. Those are the only classes written in TS; Tailwind does not read TS
>   (point 2), so each is listed in `theme.css`'s `@source inline(…)`.
> - **`:host` in the `.css` only when it depends on something**: a state
>   (`:host(.in-drawer)`, `:host(:hover)`), a media or container query, a context
>   (`:host-context(.grid--fill)`), or a layered default a caller must be able to
>   override (`lib-icon`).
> - **Never `style` in the host metadata.** It beats every class, so the caller
>   can no longer place the component with a utility, and it has no states.

**Why not `:host` everywhere**, which also works: it keeps a file per component
for one declaration, and an unlayered `:host` wins over the caller's utilities on
the same element, while a host class competes with them as an equal. The cost
of the choice is the list in `theme.css`, which nobody would keep in step by
hand — so `design-host-classes.spec.ts` fails when a component's host class is
missing from it, or when a component stylesheet holds nothing but comments.
Measured: removing `contents` from the list turned it red on the panes that use
it; a comment-only `totals.css` turned it red on that file.

- **What moved**: 93 stylesheets deleted (25 empty, 68 `:host` only) and the
  unconditional `:host` of 7 mixed ones, whose stylesheets keep the rest
  (anchoring, `::after`, the wordmark a test reads). The `:host` rules that
  depend on a state or a query stayed: the backoffice's nav, shell and worklist
  pane, the storefront's product card, shop menu and filter chips.
- **Checked against the callers**: no template puts a `display`, `position` or
  `overflow` utility on one of these hosts, so no two utilities of one property
  meet on an element, where Tailwind's order and not the author's would decide.

## Amendment (2026-10-01, later the same day): the bridge is gone

Point 1's `@theme inline reference` block — the bridge from
`design/tokens.css`'s own names into Tailwind's — is replaced by ADR 0050: the
tokens are declared in Tailwind's namespaces in `design/theme.css`, which the
apps import, and `design/tokens.css` is generated from it for the pages with
no Tailwind. Tailwind's default theme is no longer imported at all, so
`--*: initial` went with the bridge. Every size the templates name is a token
since then (`min-h-tap`), and two specs hold it: no lone length in brackets,
and every class a template names builds.
