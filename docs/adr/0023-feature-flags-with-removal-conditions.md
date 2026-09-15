# ADR 0023 — Feature flags are a closed set, each with a removal condition and one reader

Status: accepted · 2026-09-15 · Amends [ADR 0004](0004-lexical-fallback-per-language-indexes.md) · Cashes the reservation in `roadmap.md`

## Context

ADR 0004 said every AI layer would sit behind a feature flag and degrade to
lexical search, and for a phase and a half there was no flag mechanism for it
to sit behind. Phase 8 made the gap concrete: hybrid search needs a switch that
decides, for the storefront, the MCP tools and UCP at once, whether a query is
answered by words or by words and meaning — and `HybridDegradesToLexical`
cannot be written without one.

Article 07 argues the other side, and this repository agreed with it: **a flag
guards behaviour that exists and can be turned on**. A flag over code nobody
wrote is configuration promising a feature. The roadmap planned five —
`SearchVector`, `SearchRerank`, `SearchMultimodal`, `UcpTransactional` and
`Copilot` — and three of those would each guard nothing today.

The two ways flags rot are well known. One is never removing them, so the
codebase grows a permanent second path per experiment. The other is reading the
same flag in several places, so the storefront can be hybrid while an agent
tool is lexical and nothing says which is right.

## Decision

**Flags are the members of one closed enum, `FeatureId`, in the SharedKernel.
Each carries the measurable condition under which it is deleted, and each is
read in exactly one place in production code.** Two members, not five:

| Flag | Its one reader | Deleted when |
|---|---|---|
| `SearchVector` | `SearchProductsHandler`, which picks the keyed strategy | hybrid clears its own thresholds and beats lexical on NDCG@10 in both cultures in three consecutive gate runs |
| `SearchRerank` | `HybridSearchStrategy`, before its rerank stage | the reranker raises hybrid's NDCG@10 in both cultures and answers inside its budget on a CPU too — or together with the reranker, if it never does |

- **`[RemovalCondition]` is required** on every member, and an architecture rule
  refuses a member without one — or with one too short to be a condition.
- **One reader per flag**, held by a second rule that searches `src/` for
  `FeatureId.<Name>`. Read nowhere, the flag goes; read twice, it becomes one
  decision whose result is passed on.
- **`IFeatureGate` is a port** in the SharedKernel, because the kernel knows
  nothing about where configuration lives. `ConfigurationFeatureGate` in
  ServiceDefaults reads `Features:<Name>` on every call, so a reloaded value
  takes effect without a restart, and **anything that is not exactly `true` is
  off**: an unset flag is the default, and the default is the path with no AI in
  it.
- **Not `Microsoft.FeatureManagement`.** It is MIT and allowed, and it brings
  targeting, filters and variants for a closed set of two booleans. The
  dependency budget is small on purpose.
- **The AppHost writes each flag with the switch that starts what it guards.**
  `SearchVector` is on when `Tendero__Vectors=true` starts Qdrant and the model
  server; `SearchRerank` only when the reranker starts too, which is only on a
  GPU. A flag on over a stopped container degrades every search, and a container
  running under a flag that is off is memory doing nothing.
- **The evaluation tool does not read configuration for flags.** It asks each
  strategy by name, and reranking is on exactly when a reranker was given.

The other three flags enter with the phase that builds what they guard:
`SearchMultimodal` in phase 13, `UcpTransactional` in phase 11, `Copilot` in
phase 12. Each arrives with its own condition.

## Consequences

- A flag costs a line in an enum, a sentence saying how it dies, and one call
  site. Adding a second reader is a red build that says what to do instead.
- **The first measurement did not meet `SearchVector`'s condition**
  (`docs/search-evaluation.md`, 2026-09-15): hybrid scored en 0.944 against
  lexical's 0.935 and es 0.912 against 0.927. The flag stays off by default,
  which is exactly what a condition written before the numbers is for.
- **`SearchRerank` measured worse than no reranker** on the same day — en 0.912,
  under its threshold. Its own condition names the outcome in which it is deleted
  together with the reranker; one run is not three, and the decision waits for
  the gate rather than for a feeling.
- The degradation test and the flag are separate on purpose: turning a flag on
  never removes the fallback, because a vector layer that is down still answers
  with lexical search. The flag decides what is asked for; invariant 8 decides
  what happens when it cannot be had.
- Roadmap D's "five flags" is superseded by this table. The rule that a test
  asserts every value has exactly one production call site is kept as written.
