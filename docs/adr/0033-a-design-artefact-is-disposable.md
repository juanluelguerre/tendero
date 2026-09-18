# ADR 0033 — A design artefact is disposable, and the design tool is an experiment with a removal condition

Status: accepted · 2026-09-18 · Related: [ADR 0009](0009-spec-driven-development-scope.md), [ADR 0023](0023-feature-flags-with-removal-conditions.md), [ADR 0006](0006-dependency-baseline.md)

## Context

The question was whether an agentic design canvas — pen.dev, whose `.pen` files
are documented JSON read by a coding agent over MCP — belongs in this project's
lifecycle, and whether a sketch is still necessary at all when an agent produces a
navigable prototype in minutes.

Answering it found that **this repository already ran the experiment and lost the
result**. `docs/design/` held two design documents written on 2026-09-03 that
nothing referenced, that pointed at three files which do not exist, and that state
in their own text what they are: round one *"eran de otro producto (reparto de
barrio)"* and round two *"Reconstruido desde el código"*. One designed a different
shop; the other was drawn after the screens. The day after, PR #1 shipped the
screens article 18 calls correct and not a shop.

Between them they carry **575 hand-written hexadecimal colours**, against the
design system's only hard rule. The test that enforces that rule walks the whole
repository and keeps `.css` files, so an `.html` artefact was outside it by
construction.

pen.dev's limits were measured rather than argued, on 2026-09-18: `read_skill()`
and `get_app_state()` both answered `A file needs to be open in the editor`. The
MCP server needs the signed-in desktop application open with a document in it —
nothing headless, nothing in CI, nothing a fresh clone reproduces. The tool is
free today with paid plans stated as possible, and its format documentation
reserves the right to make breaking changes.

## Decision

**1 · A design artefact is disposable, and it is not committed to the public
repository.** It lives under `docs/blog/reports/`, with the material it is
material for, which the publication filter already removes. When the article and
the book chapter that use it are written, it is deleted — the rule this repository
already applies to diagram sources: *a published artefact is what was published*.

This is the opposite of the tool's own pitch, that design files belong in git, and
the reason is the artefact's job rather than the tool's quality. A wireframe earns
its keep by being cheap to throw away. A file kept in a public repository is a
file somebody has to maintain, in a format whose owner reserves breaking changes,
readable only by an application that asks for an account.

**2 · `design/tokens.css` stays the single source of truth.** A canvas may mirror
token values; it may not mint one. A static artefact in colour is a *copy* of the
design system, and a copy with no test drifts in silence — which is what the two
mockups did, all the way to describing a different product.

**3 · pen.dev enters as a controlled experiment, never as adoption**, in ADR
0009's shape: one branch, one screen, the comparison published either way. It is
tooling and not a dependency — no package enters — so the two-tier licensing rule
of ADR 0006 does not apply to it; what applies is that discipline.

The experiment is declared in full in `docs/blog/notebook.md`
(*The mockups nobody read*), before it runs: the blind arm designs the backoffice
search-evaluation panel from `P8-11`'s statement, the shape of
`GET /api/search/evaluation`, `design/DESIGN.md` and `design/tokens.css`, **without
opening the Angular component**, and is judged against four criteria fixed
beforehand — tokens, structure out of three, one kept decision, cost.

**4 · The removal condition**, in ADR 0023's idiom: if the experiment is refuted,
the tool goes and the measurement stays. Nothing in `src/` or `frontend/` may come
to depend on a `.pen` file existing.

**5 · Not in phase 11.** ADR 0009 reserves the Spec Kit experiment for the UCP
transactional work. Two new methods inside one phase make the result attributable
to neither.

## Consequences

- `docs/design/` no longer exists. Its two files are evidence under `docs/blog/reports/2026-09-18-design-before-code/`, and the public repository loses two documents that rendered incompletely and that no page linked.
- The design-token test is unchanged and still reads `.css` only. Widening it to `.html` would fail the diagram sources, which copy the palette for a stated reason: they must render standing alone before being exported to PNG. The gap is recorded here rather than closed.
- A screen may now be designed before it is coded, and the artefact that records it has a defined end of life. Nothing obliges a screen to be designed that way.
- If the experiment is accepted, what enters the repository is the Angular component, as always — never the canvas file.

## What was rejected

- **Committing the `.pen` file**, which the tool recommends. It would put a format with reserved breaking changes into a public reference repository, needing an account to open, for an artefact whose value is being thrown away.
- **Adopting the tool for the lifecycle** rather than for one screen. Its MCP server cannot run unattended, so it cannot be a gate, and every other quality rule in this repository is one.
- **Running the experiment on a screen that does not exist yet** (phase 10's claim review queue). It would have measured design-before-code more purely, and it would have had no control arm to compare against.
- **Widening the token test to every file that can carry a colour.** It would be red on six diagram sources that are right as they are.
