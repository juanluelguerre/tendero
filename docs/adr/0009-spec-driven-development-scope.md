# 0009 — How far to take spec-driven development

**Status**: accepted

## Context
`initial-plan.md` §8 chose a lightweight spec discipline (CLAUDE.md as
constitution, one spec per feature in `docs/specs/`, ADRs for constraining
decisions) and left "full Spec Kit ceremony evaluated later" open. Phase 1 is
now built, so the question has a concrete answer instead of a hypothesis.

Spec Kit (GitHub) drives **spec → plan → tasks → implement** through agent
commands, installs a `.specify/` tree with its own `memory/constitution.md`,
templates and scripts, and ships a guide for adopting it in an existing
codebase.

## Decision
**Keep the lightweight discipline through phases 1 and 2. Run Spec Kit as a
controlled experiment on the UCP + MCP server in phase 3, on its own branch,
and publish the comparison.**

Why not in phase 1: the slices arrived already designed — `ImportProducts` came
with its handler, validator, batching and telemetry decided — so `/specify` and
`/plan` would have documented settled decisions rather than discovered
anything. And CLAUDE.md is already the constitution, enforced by architecture
tests; a second `constitution.md` is the duplicated source of truth the
invariants exist to prevent. `docs/specs/TEMPLATE.md` is also better adapted
than the generic template: it carries **Measures** (tests to add, NDCG
thresholds affected) and **Article**, both Tendero-specific.

Why the UCP server: it implements an external protocol, it is the most
underspecified surface in the roadmap (catalog search, cart, identity,
checkout, order management, AP2 mandates), and it is the headline contribution
— there is no .NET reference implementation. Acceptance criteria *are* the
product there. Phase 2's human review queue is the runner-up candidate.

Rules for that experiment, so it stays reversible:

1. **One constitution.** `.specify/memory/constitution.md` is generated from or
   points at CLAUDE.md; the two are never maintained in parallel.
2. **One branch, one feature.** Not a repo-wide adoption.
3. `/specify` states what and why only. `plan.md` will come out thin because
   CLAUDE.md already fixes the stack — that is the constitution working, not a
   gap to fill.
4. **Architecture decisions stay in ADRs**, never in a per-feature `plan.md`,
   or decisions that constrain the future end up buried.
5. `tasks.md` is reviewed by hand before implementing; it is a PR-sized
   checklist, not an unattended script.
6. Specs are living documents: when reality diverges, the spec is updated —
   the same rule this repo already applies to the plan.

## Consequences
Phase 3 produces an article comparing both methods with real evidence, which is
worth more to the project's stated goals than adopting a toolchain silently. If
the experiment fails, nothing outside one branch has to be undone. Verify Spec
Kit's current command set before starting: it moves fast, and the published
docs disagree on which steps exist.
