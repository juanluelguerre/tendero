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

**1 · A design artefact does not enter the public repository, and it is kept as
evidence.** It lives under `docs/blog/reports/`, with the material it is material
for, which the publication filter already removes.

> **Amended the same day, by the author: it is not deleted.** The first draft of
> this record said the artefact was deleted once the article and the chapter using
> it were written, by analogy with diagram sources — *a published diagram is what
> was published*. That analogy was the wrong one, and it contradicted this
> repository's own practice. A diagram source is deleted because its **export
> replaces it**; a `.pen` is replaced by nothing. It is evidence of a measurement,
> and evidence here is kept: `docs/blog/reports/2026-09-16-hybrid-search/` still
> holds its Stryker reports and evaluation logs, and that article is not even
> published. **Deleting one is a decision somebody takes, never an automatic
> consequence.**

What survives from the original argument is the half that was never about
deletion: a design artefact **is** disposable in the sense that matters — it may
be thrown away without anything breaking, nothing in `src/` or `frontend/` may
depend on it, and it is never a source of truth. Keeping a copy of it costs
nothing and answers questions later.

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

**4b · And what acceptance looks like, which the first draft of this record left
out.** A dossier under `docs/blog/reports/` is the right home for the *evidence of
one experiment*, and the wrong home for *a step of the lifecycle that repeats*. So
if the verdict is accepted and designing before coding becomes a practice rather
than a trial, the artefacts move to a permanent home under `design/`, beside the
system they are drawn against, and this record is amended to say so.

> **Amended 2026-09-18, after the run: the experiment was accepted.** Criterion 1
> held (589 nodes, 0 literal colours), criterion 2 reached 3 of 3, criterion 3
> yielded two decisions the author keeps, and the cost was 12 min 20 s with no
> human intervention during the run. The full scoring is `verdict.md` in the
> dossier. So the permanent home is now owed, under the two constraints below —
> and **the artefact it holds is a mockup, not a sketch**: static, medium
> fidelity, in colour, built on the real tokens, which is the mockup rung by the
> definition this project is using as its baseline. `design/sketches/` is misnamed.
>
> Three things the accepted run found that this record did not anticipate, each
> written up in `verdict.md`: the shipped panel covers **six states to the blind
> pass's four**; **a written specification rots** and a design drawn from it
> inherits the rot (the one rejected candidate was faithful to a brief frozen from
> a document that had gone stale the day before); and the pass surfaced a
> **shipped accessibility defect** — `[data-theme='dark']` redefines `--positive`
> but neither `--danger` nor `--warning`, so `.tag--danger` ships at about 2.6:1
> on the dark backoffice.

Two constraints on that home, both learned rather than assumed:

- **It is named for what it is, never for the tool.** `design/sketches/`, not `design/pen/`. A folder named after a product outlives the product — the objection article 07 is entirely about — and the thesis of this whole experiment is that the surviving rung is the *sketch*, which is a role and not a vendor.
- **What lives there is the exported HTML, not the editable `.pen`.** `design/` is public and is not touched by the publication filter, and a fresh clone must be able to open what it finds — article 06's thesis. A proprietary file that needs an account to read fails that; a one-click HTML export does not. The `.pen` stays with the evidence, in the private half.

Until the verdict exists, none of that is built. Creating the permanent home before
knowing whether the practice earns one is the mistake this repository already
wrote an article about.

**5 · Not in phase 11.** ADR 0009 reserves the Spec Kit experiment for the UCP
transactional work. Two new methods inside one phase make the result attributable
to neither.

## Consequences

- `docs/design/` no longer exists. Its two files are evidence under `docs/blog/reports/2026-09-18-design-before-code/`, and the public repository loses two documents that rendered incompletely and that no page linked.
- The design-token test is unchanged and still reads `.css` only. Widening it to `.html` would fail the diagram sources, which copy the palette for a stated reason: they must render standing alone before being exported to PNG. The gap is recorded here rather than closed.
- A screen may now be designed before it is coded, and the artefact that records it is kept as evidence in the private half rather than published. Nothing obliges a screen to be designed that way.
- If the experiment is accepted, what enters the repository is the Angular component, as always — never the canvas file.

## What was rejected

- **Committing the `.pen` file**, which the tool recommends. It would put a format with reserved breaking changes into a public reference repository, needing an account to open, for an artefact whose value is being thrown away.
- **Adopting the tool for the lifecycle** rather than for one screen. Its MCP server cannot run unattended, so it cannot be a gate, and every other quality rule in this repository is one.
- **Running the experiment on a screen that does not exist yet** (phase 10's claim review queue). It would have measured design-before-code more purely, and it would have had no control arm to compare against.
- **Widening the token test to every file that can carry a colour.** It would be red on six diagram sources that are right as they are.
