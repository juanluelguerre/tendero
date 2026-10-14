# 0060 — A recommendation is solved, not generated

**Status**: accepted (phase 13c, written at its close, 2026-10-14). The decisions
are the kit solver's spec (`docs/specs/kit-solver.md`, *Decisions taken with the
author*, 2026-10-09). What building it added is marked *found while building*.

## Context

Differentiator 5 is a recommendation with constraints that shows its arithmetic:
*a kitchen kit for under 120 €*. A model asked that answers at once and cannot
be trusted with it. It picks products by how they read, adds prices
approximately, and explains its choice with reasons written afterwards. Nothing
in that answer can be tested, and a shop that adds a total wrongly in front of a
customer has lost them.

The shop already has everything a right answer needs: what each product is,
what it costs, whether it can be bought, what works on an induction hob. What it
lacked was the step that puts them together under a budget.

## Decision

**A recommendation is a constrained search solved by code. A model may stand at
its two ends only.** Four stages:

1. **Parse**: a form, or a sentence. *Found while building*: the model does not
   fill the request. It answers one closed question, *which of the shop's kits*,
   and code reads the budget and the requirements written in the sentence
   (`KitSentenceReading`, K-6; ADR 0055's closed question).
2. **Retrieve**: the shop's own search, as it is, per slot.
3. **Solve**: a pure function (`Search/Kits/KitSolver`): no clock, no I/O, no
   model. An architecture rule holds its namespace to the shared kernel, and
   CsCheck holds its properties: never over the budget, the same kit in any
   order, and as good as trying every kit there is.
4. **Explain**: sentences written by code from the solver's trace, in es and en.
   The trace is the explanation's only source.

**The objective, in this order**: required slots filled, then optional slots
filled, then the sum of relevance, then the lower total, then the candidates'
codes. Coverage comes before relevance, the roadmap's order reversed: a kit that
drops its pan to afford a better knife is not a kit. The last criterion makes
two kits that are as good as each other always the same kit.

**Exact, with a ceiling that says so.** Branch and bound, exact within a ceiling
on the nodes explored. A solution that reached the ceiling says it did, and is
then *the best found*, never presented as the best there is. An unfillable kit
is an answer: each slot left empty is named with its reason (*no candidate*, or
*over budget by 14.90 €*).

**Where it lives: in Search**, not in Knowledge as the roadmap said. Knowledge
knows the model port and the kernel, and cannot ask the search for a candidate.
A kit's candidates are search results and its relevance is rank.

**The budget is held against the prices shown.** Promotions are shown after the
kit is built, not solved into it. Solving against the quoted total would call
Pricing at every node and the solver would stop being pure.

*Found while building*:

- **The same ask must give the same kit.** It did not, twice within a minute: a
  cold hybrid search degraded the first kit to lexical, and equal scores fell in
  any order. The hybrid search is warmed at start
  (`WarmHybridSearchAtStartupWorker`), and runs of equal scores are ordered by
  code (`BuildKitHandler.TiesByCode`), and only those runs: a reranker's order,
  which need not follow the scores, stays its own.
- **Agents get the same function.** `list_kits` and `build_kit` on the MCP
  server, read-only and anonymous like its other tools, with the budget in minor
  units (ADR 0059).

## Consequences

- A kit can be tested like any function, and was: properties, a laboratory
  catalogue, and hand mutations behind every test added to existing code (`docs/testing.md`,
  *Phase 13c's close*).
- The explanation can only say what the trace holds. A model that rewords it
  (decision 6) would sit behind a grounding check. It has not been built: where
  it lives, and whether the page needs it, are still the author's to decide
  (the spec, *Open*).
- "For two" is left out and said to be (decision 4). A slot that declares a
  per-person attribute needs numbers that a search hit does not carry.
- Templates are the shop's, in a committed file. The shopkeeper creating them in
  the backoffice makes a template state with a writer, and is in the standing
  backlog.
