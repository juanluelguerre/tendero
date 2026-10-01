# 0050 — The design tokens are Tailwind's theme

**Status**: accepted (the author chose it on 2026-10-01, option 2 of three);
built on `refactor/tailwind-utility-first`. Supersedes the token file's form in
ADR 0033 and the bridge of ADR 0049, keeps both of their principles.

## Context

ADR 0049 made the theme the tokens by **bridging** them: `design/tokens.css`
declared them under its own names in `:root`, and the apps' `theme.css` mapped
each into Tailwind's namespaces in an `@theme inline reference` block —
`--color-surface: var(--bg-surface)`, `--spacing-5: var(--space-5)`. Where the
two names happened to agree, the bridge read `--radius-sm: var(--radius-sm)`:
25 lines that looked like a variable defined as itself. The author asked
whether Tailwind or Angular recommended that. Neither does. Angular has no view
on it; Tailwind documents `@theme inline` for variables a project already has,
which is what the bridge was, but its own pattern for a design system is that
the tokens **are** the theme: declared in `@theme`, written out as CSS
variables by Tailwind itself, with nothing between them and the utilities.

The bridge also had a cost nobody saw until the review of the same day: every
token added had to be remembered twice, and 275 sizes in the templates had
never been tokens at all (`min-h-[2.75rem]` seventy times, the 44px a finger
needs), so "a utility can only name a token" held for colours and not for sizes.

Three options were put to the author: keep the bridge and explain it; generate
the bridge from the tokens; or make the tokens Tailwind's theme and generate
the plain file the pages without Tailwind need.

## Decision

**The third.**

1. **`design/theme.css` is the source**: one `@theme static` block in
   Tailwind's namespaces — `--color-*`, `--font-*`, `--text-*`, `--leading-*`,
   `--tracking-*`, `--spacing-*`, `--radius-*`, `--shadow-*`, `--ease-*`,
   `--container-*`, `--breakpoint-*` — then the dark theme's override and the
   element rules, as before. `static` writes every token out, because
   stylesheets, the charts and the dark theme read them by name, not only the
   utilities a template happens to use. What Tailwind has no namespace for
   (`--dur-*`, `--underline-offset`) sits in a plain `:root`.
2. **`design/tokens.css` is generated** from it by `tools/tokens.mjs` — the same
   text with `@theme static` as `:root` — for the landing, the walkthrough and
   the prototypes, which have no Tailwind and link it as they always did. CI
   regenerates and diffs it, as it does the API types and the tracking plan.
3. **The apps import Tailwind without its default theme**: `preflight.css` and
   `utilities.css` into declared layers, never `theme.css`, so its palette,
   spacing and breakpoints do not exist and `--*: initial` is no longer needed.
4. **Every size the design uses on purpose is a token**, under a name that says
   its role: targets (`tap`, `target`), controls, icons, marks, thumbnails,
   columns, a hairline and a rule, text measures (`max-w-measure-lg`), panel
   widths, the breakpoints the CSS already turned on. A layout written as an
   expression — `grid-cols-[minmax(0,1fr)_320px]` — stays in brackets, as
   Tailwind intends them; a lone length does not.

## Consequences

- One source, no bridge, no line that defines a variable as itself; a token
  added is a utility at once (`--spacing-col-action` is `w-col-action`).
- The rename touched 45 files (CSS, templates, the two chart readers, the
  landing, the walkthrough, the design specs) and none of the history: ADRs,
  specs, the blog and the book keep the names they were written with.
- **Two guards**, both run with the unit tests:
  - `design-arbitrary-values.spec.ts` fails on a lone length in brackets.
  - `design-utilities.spec.ts` asks Tailwind's own design system — the API its
    Prettier plugin and editor tooling load, marked unstable, so an upgrade
    that changes it fails loudly — whether every class in a template builds,
    unless a stylesheet defines it or a test selects it. It exists because the
    move lost `font-bold`, `p-0` and `bg-transparent` without a sound: the
    bridge had declared the weights, `--spacing-0` and `--color-transparent`,
    the new source at first did not, and Tailwind ignores a class it cannot
    build. The Prettier plugin noticed first, by sorting `font-bold` to the
    front, which is where it puts what it does not know.
- The landing needs a redeploy after the merge, like any change to the tokens.
- The token count grew by about 70 names. That is the price of the promise
  holding for sizes, and each name carries its reason in a comment.
