# ADR 0032 — Knowing that the shop does not sell something is a query-understanding decision, not a threshold on a retrieval score

Status: accepted · 2026-09-17 · Related: [ADR 0004](0004-lexical-fallback-per-language-indexes.md), [ADR 0023](0023-feature-flags-with-removal-conditions.md), [ADR 0031](0031-model-weights-are-dependencies.md)

## Context

Hybrid search put ten products on the first page of every golden query the shop
has nothing for; lexical put 1.4 (es) and 2.6 (en). Three declared thresholds on a
retrieval score were refuted (`docs/search-evaluation.md`):

- **H1, a cosine floor**: no gain on `holdout`, and `shown@10` stayed at 10.0.
- **H4, a reranker floor per result**: about 0.2 of NDCG@10 lost on every other
  query, recall@50 0.939 → 0.666.
- **H5, a reranker floor per query**: it emptied "regalo abuela" and "gift for
  grandma" — real answers the reranker scores as low as a lamp for a phone.

Research agreed with the failures: shops say plainly that nothing matches
([Nielsen Norman Group](https://www.nngroup.com/articles/search-no-results-serp/)),
score cutoffs are relative and cannot say "nothing"
([Algolia](https://changelog.algolia.com/control-the-precision-recall-tradeoff-for-neuralsearch-2u4llm)),
and "we don't sell that" is decided by classifying the query into a product type
([Amazon Q2PT](https://arxiv.org/html/2410.07121v1)).

## Decision

> Layer B below was **amended the same day** — it is evaluation tooling now, and
> `SearchAnswer` has two values. See *Amendment, 2026-09-17*.

**Every search page carries an answer, decided beside retrieval and never by a
threshold on its scores.** `SearchAnswer` is `Exact`, `Related` or `OutOfScope`,
with an optional help topic; it is null for a browse or a date sort, and it runs
after whichever strategy answered, so lexical is as honest as hybrid and no
ranking changes.

- **Layer A, no model, always on.** `Exact` when an active product carries every
  word of the query in what it is — `name`, `brand`, `attributesText`,
  `categoryPathText`, no description, no fuzziness — otherwise `Related`. The count
  is one clause per word because `brand` has its own analyser (the evidence is in
  `search-evaluation.md`), so a typo gets the notice by design. A help topic exists
  only if its destination does: today `returns` → `GET /api/returns/policy`.
- **Layer A ignores a bare measurement**, amended 2026-09-17 after measuring where
  the notice lands. The catalogue writes "6 litros" and "24 cm"; shoppers write "6l"
  and "24cm", so that token matched nothing and the page said "nothing matches
  exactly" above the right pan. A query word that is a number with at most a short
  unit glued to it (`^\d+([.,]\d+)?[a-z]{0,3}$`, case-insensitive) is dropped from
  the full-match check — from that check only, since the search still ranks with the
  whole query — and if every word would be dropped, none is, so the check is never
  empty. It silences six notices, on pages answered at 0.634 to 1.000, and nothing
  honest: the notice rate goes 0.538 → **0.481** (en) and 0.519 → **0.462** (es)
  with NDCG@10 and recall@50 unmoved. What it costs is that the notice goes blind to
  a **size** mismatch — a shop holding only a 4 L pot, asked for a 6 L one, no longer
  admits it on the litres. At a hundred products, where sizes are a closed attribute,
  that is the cheaper error; the version that does not pay it normalises "24cm" into
  "24 cm" at index and query time, which is an analyser change and a full reindex.
- **Layer B, scope by anchors, refuted.** It calls a query out of scope when its
  best cosine to 30 categories the shop does not sell beats its best cosine to the
  23 it does by a margin. By a protocol declared beforehand, in-scope queries were
  marked out of scope 9 / 5 / 1 / 1 times at margins 0.02 / 0.05 / 0.08 / 0.12, so
  no margin was chosen. **Its code is kept off — with no calibration it never
  registers — and the author decides whether it stays.** The protocol, a first
  attempt that gave no verdict and the latency are in `search-evaluation.md`.
- **Exit condition.** Past a few hundred categories, with interaction logs, or on a
  failure a trained model could fix, this becomes a trained query classifier — a
  backlog row in `docs/delivery-plan.md`.

## Consequences

- In the third gate run every no-answer query is admitted (1.00) and false
  out-of-scope is 0 — trivially, while layer B is off. Hybrid still shows ten
  products on those pages, now labelled as the closest things.
- The notice lands on 0.481 (en) / 0.462 (es) of queries that do have answers —
  0.538 / 0.519 before bare measurements were ignored: honest, frequent, and an open
  decision for the author.
- MCP returns `answer`; UCP search adds `no_exact_match` and `help_available`
  messages (`not_sold` is never sent while layer B is off).
- The five knobs behind H1–H5, including H2's fusion weight, are deleted.

## Amendment, 2026-09-17 — the classifier is evaluation tooling, the harness is the part that stays

Layer B stayed in `src/Search`, off, for one evening. It is out of the shipped
code now, and out of the API's shape: `SearchAnswer` is **`Exact` or `Related`**,
`outOfScopeLabel` is gone from the wire, and UCP's `not_sold` message with it.

The reason is that "kept off" could not become "kept on" by anything short of a
new measurement, and meanwhile it cost three things. **It could never
register**: the registration wanted a calibration file, no protocol run had
produced one, and none would without a better signal — so `AnchorQueryScope`,
`NoQueryScope` and the registration branch were unreachable code. **The contract
carried a value nothing could produce**: `OutOfScope` was in the OpenAPI
document, in the generated TypeScript, in a storefront branch and in a UCP
notice, and every one of them was dead. And **the mutation survivors could not be
closed**, because a test cannot kill a mutant in a path no shipped code takes.

What survives is the exam, not the answer. `AnchorQueryScope`, the decision rule,
the anchor file, the protocol's selection and acceptance rules and `ScopeSet` are
in `tools/SearchEval/QueryScope`, and `dotnet run --project tools/SearchEval --
--suite scope` still runs the whole protocol against a model server. The trained
classifier the exit condition names will be measured on the same 40 scope query
pairs per culture, by the same rules — which is what makes layer B a baseline
rather than a memory.

The experiment itself is in the history, in the commit *"feat(search): say what
kind of answer a page is, and make hybrid the default"*, and its evidence is in
`docs/blog/reports/2026-09-17-no-answer-research/layer-b-evidence.md`. Nothing
about the refutation changes: the numbers above stand, and the article on this
research is written from them.

## What was rejected

- **A sixth threshold** on splits already looked at too often.
- **Relative cutoffs**: they trim a tail and never say "nothing".
- **An LLM classifier**: the first `IChatClient` for a decision that needs no
  generation, and zero-shot LLMs trail fine-tuned models at out-of-domain detection
  ([arXiv 2310.10176](https://arxiv.org/abs/2310.10176),
  [arXiv 2305.13512](https://arxiv.org/abs/2305.13512)).
