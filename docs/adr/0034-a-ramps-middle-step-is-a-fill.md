# ADR 0034 — A ramp's middle step is a fill; only its dark step carries text

Status: accepted · 2026-09-20 · Related: [ADR 0033](0033-a-design-artefact-is-disposable.md), [ADR 0023](0023-feature-flags-with-removal-conditions.md)

## Context

The accessibility pass the board had been holding since 2026-09-19, when laying
the design system out on a canvas (ADR 0033) found a shipped defect it was not
looking for: `[data-theme='dark']` redefined `--positive` and neither `--danger`
nor `--warning`, so `.tag--danger` shipped at 2.56:1. Those two were repaired the
same week. The row that stayed open said an accessibility pass over the whole
palette was the honest unit of work, not six one-line fixes, and it was right for
a reason it could not have known.

**Every ratio was recomputed from `tokens.css` rather than read from that row.**
The six it listed reproduce exactly. Four more do not appear in it, and two of the
six turn out to be measured against a background that does not ship:

- `.tag` is `border: 1px solid currentColor` on a transparent background, so a status tag sits on `--bg-surface`, `--bg-raised` or `--bg-page` — never on `--amber-50` or `--olive-50`. Those pairs are the **mockup's**, not a screen's.
- Measured on the surfaces that do ship, `--positive` fails on two of the three (4.09:1 on `--bg-page`, 4.39:1 on `--bg-raised`) and passes only on `--bg-surface`. The row had it down as a single tint failure.
- `--accent` used as *text* — a link, a delta, the agent tag — was not in the row at all: 3.87:1 on `--bg-surface` and 3.43:1 on `--bg-page`.

**The finding that reframed the work: no text colour passes on `--clay-500`.**
White is 3.87:1, `--ink-900` is 4.33:1, `--clay-800` is 2.47:1. It is not a matter
of choosing the text better. The brand's mid-ramp terracotta cannot have body text
on it, in either direction, and every repair that starts from "which colour goes on
top" is looking for something that does not exist.

**And the worst ratio in the system came from a token that was not there.**
`define-variants-page.css` wrote `color: var(--on-accent)` and nothing defined
`--on-accent`, anywhere. An undefined custom property makes the declaration
invalid at computed-value time, so the colour fell through to the inherited
`--text`: 2.45:1 on the dark accent fill, with no error, no warning and nothing
red. The repository held **three different answers** to "what text goes on an
accent fill" — `--stone-0` in the storefront, `--ink-900` in the backoffice, and a
token somebody assumed existed — because there was no token that answered it once.

One more defect surfaced while reading the files rather than the palette, and it
is an absence rather than a weak ratio. Six rules wrote `outline: var(--focus-ring)`.
The `outline` shorthand resets what it omits, so `outline-style` lands on `none`
and **nothing is painted** — verified in a browser, not reasoned about:
`outline-style: none, width: 3px, color: rgb(184, 69, 31)`. Those six rules are
more specific than `:focus-visible` in `tokens.css`, so they did not merely fail
to add a ring, they **removed the one the system already drew**. `design/DESIGN.md`
says "every interactive element has a visible `:focus-visible` ring. No
exceptions."

## Decision

**1 · A ramp's middle step is a fill and a decoration; only its dark step may
carry text.** That is the rule the measurements arrive at, and it is what the new
`--clay-700`, `--olive-600` and `--amber-700` exist for.

**2 · `--clay-500` `#D85A30` does not move. It is the brand.** The logo, the
favicon, the wordmark and the awning stripe keep it, and `brand/BRAND.md` still
names it *Terracotta (primary)*. What changes is the **interface's** accent:
`--accent` is `--clay-600` now, and `--accent-hover` the new `--clay-700`.

The brand colour and the action colour stop being the same value, and that is the
decision rather than a side effect. It is defensible because their jobs differ: the
awning is decoration with nothing on top of it, and a button is a surface somebody
has to read. Outside `tokens.css` the only consumer of `--clay-500` in either app
is the awning utility, so the split cost nothing to make.

**3 · `--on-accent` is a token, because the answer differs per theme.** White
reads on the light accent (5.37:1) and not on the dark one (2.76:1); ink reads on
the dark accent (6.07:1) and not on the light one (3.12:1). Each of the two
hand-written answers already in the apps was right for its own theme and wrong for
the other, which is exactly the shape of a question that wants one token instead of
a convention.

**4 · `--text-subtle` is deleted, not darkened.** The lightest value clearing
4.5:1 on `--bg-page` is `#736D67`, **0.57 of ratio** from `--text-muted`. Under AA
the light theme's three text tiers collapse into two: a level that can only be
told apart from the one above it by failing the minimum is not a level. Its 38
readers move to `--text-muted`, which already passes in both themes.

**5 · `--focus-ring` becomes an alias of `--accent`**, which closes the last
structural gap of the 2026-09-19 note — the dark theme redefined 14 of 15 semantic
aliases and `--focus-ring` was the one left. It no longer needs redefining: the
theme attribute goes on the root element, so the alias resolves against whichever
accent won there. Measured at 4.76:1 light and 6.60:1 dark against WCAG 1.4.11's
3:1 for a non-text indicator.

**6 · The promise becomes a gate.** `design-contrast.spec.ts` parses `tokens.css`,
resolves `var()` chains per theme the way the browser does, and holds every text
token against every surface at 4.5:1, every fill pairing at 4.5:1, and the focus
ring at 3:1. Six tests; seven deliberate mutations, each turning at least one of
them red.

This is the half that matters, and it is the same argument ADR 0033 made one
format out: `design-tokens.spec.ts` keeps every colour in one file and says nothing
about whether those colours can be read. A palette with no contrast test drifts in
silence exactly the way the two orphaned mockups did.

## Consequences

- The storefront's one clay action is visibly deeper. The awning beside it is not, which is the split made visible.
- 38 component declarations move from `--text-subtle` to `--text-muted`; `--color-subtle` leaves the Tailwind bridge, and `--color-on-accent` joins it.
- Six focus rings that painted nothing now paint. This is the one repair in the pass that a contrast measurement could never have found, because the colour was always correct — it was the style that was `none`.
- `--ink-400`, `--olive-500` and `--amber-500` keep their place in the palette and lose their consumers. A ramp is a vocabulary and is allowed steps nothing spends today; `--amber-50` and `--red-600` already had none. `--ink-400`'s comment no longer claims it is for hints and placeholders, because it is too light for text on any surface this system has.
- The two mockups under `design/unpublished/mockups/` named `#7c746c`, the deleted dark `--text-subtle`, in 103 places. `design-mockups.spec.ts` went red on them, which is the gate ADR 0033 built doing its job on the first token change after it existed. They name the dark `--text-muted` now.
- A dark `--danger` at 4.52:1 on `--bg-raised` is the thinnest margin left in the system. It passes, it was approved a week ago on a different pair, and it is written down here rather than moved again.
- **The migration itself shipped a defect, and it is the most useful thing the pass produced.** The script that moved 51 declarations spliced two of them wrong — it collected its matches against the original text and then mutated the string it was indexing into, so every replacement after the first in a file landed two characters off. `shell.css` was the only file with three. A browser drops a malformed declaration in silence, so the language switch lost its background and the cart badge its colour with a clean build, green tests and a passing contrast audit: the text fell back to an inherited colour that happened to be legible. It was found by driving the real application in a browser, not by any gate. `design-css-integrity.spec.ts` is that gate now — a `var()` with no fallback must name a property that exists, and every `var()` must open on a property reference at all, because `var(--accencolor:` balances its parentheses perfectly. Its first draft killed one of the three mutations and was green, which is recorded in the file beside what it still cannot see.

## What was rejected

- **Darkening `--text-subtle` and keeping three tiers.** It passes at `#736D67`, with a margin of 0.03 over the bar — thinner than the margin this project insists on for its own search gate — and buys a distinction of 0.57 of ratio that nobody can see.
- **Keeping `--accent` at `--clay-500` and adding a separate `--accent-text`.** It leaves two accent tokens whose difference is invisible until somebody picks the wrong one, and it would have migrated 26 usages by hand to fix what one alias fixes. It was offered to the author with both previews and declined.
- **Moving `--clay-500` itself.** It is the brand, it is in the logo files, and repointing it would have changed the awning to fix a button.
- **Repainting the mockups to the new palette.** They are evidence of a pass on a given day (ADR 0033), not a mirror kept in step. Only the colour the system no longer owns was changed, which is the minimum the gate asks for.
- **Treating the six tint pairs the board listed as shipped failures.** They are the mockup's. Fixing a tag against a background nothing renders would have been work with no screen behind it — the failure this repository keeps writing articles about.
