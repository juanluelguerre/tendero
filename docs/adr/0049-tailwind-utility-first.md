# 0049 — Tailwind, utility-first, over a theme that is the design tokens

**Status**: accepted (the author chose it on 2026-09-30, after a survey of how
both apps used Tailwind; being built on `refactor/tailwind-utility-first`)

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

- The three-files convention stands (ADR 0047 and `CLAUDE.md`): a component's
  `.css` is still there, and usually short or empty.
- BEM names disappear from templates as they migrate; a class that remains is a
  hook for a test or a stylesheet, and says so.
- Templates get longer. That is the trade Tailwind makes, and why point 4 matters.
- Raw media queries become the theme's breakpoints, defined from the widths the
  CSS already used (640px, 64rem, 39.99rem), so the migration does not move them.
