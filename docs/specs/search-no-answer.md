# Search answers: what the shop says when it has nothing, and closing hybrid search's open decisions

Date: 2026-09-17 · Status: approved design, awaiting implementation plan · Branch: `feature/search-answers`

## Why this exists

Phase 8 left four decisions open before hybrid search could become the default
(`docs/delivery-plan.md`, *Next task*):

1. **Queries the shop has nothing for.** Hybrid search shows ten products for all
   eight no-answer queries in the golden set, where lexical shows 1.4 (es) and 2.6
   (en). Three declared hypotheses tried to detect them with a threshold on a score
   and were refuted: a cosine floor (H1), a reranker floor per result (H4), a
   reranker floor per query (H5). H5 emptied "regalo abuela", "gift for grandma",
   "staying dry on the bike", "organising the kitchen" and "picar verduras" at the
   lowest value tried, because the reranker scores their right answers as low as a
   lamp for "móvil para mi madre de 70 años".
2. **`SearchVector`**: its removal condition is two gate runs from met.
3. **`SearchRerank`**: its removal condition asks for a reranker that works on a CPU,
   and two measurements say none does (candle 35 s; ONNX INT8 fast and adding
   ~0.005 NDCG).
4. **Five configuration knobs** that exist only for the refuted hypotheses.

Before designing, the author asked how real shops solve the first one. Two research
reports answered it (private evidence: `docs/blog/reports/2026-09-17-no-answer-research/`):

- **Shopper-facing practice** (Baymard, Nielsen Norman Group, vendor docs): never a
  dead-end page and never stand-ins passed off as answers. Say plainly that nothing
  matches, offer *labelled* alternatives, and send non-product queries ("returns") to
  content. 34 % of Baymard's test users searched for non-product content, and
  non-product search is the query type sites fail most (66 %).
- **Detection**: every vendor semantic cutoff is relative to the query's own top
  result (Algolia NeuralSearch, Weaviate autocut) and trims tails; none is designed
  to answer "nothing". Query performance predictors correlate weakly (NQC r ≈ 0.2–0.35,
  cross-encoder features r between −0.17 and 0.07). **Shops decide "we don't sell that"
  in a separate query-understanding step** — classifying the query into a product type
  or category (Amazon's Q2PT, Instacart's query understanding), not by thresholding
  retrieval.

H1, H4 and H5 failed for the reason the literature predicts: they asked the retriever
to be the judge.

## Decisions

| # | Decision |
|---|---|
| D1 | No-answer handling is built in **two layers**: A, deterministic and always on; B, an anchor-based query-scope classifier, built as a declared experiment that is kept only if it passes, and deleted if it does not. |
| D2 | **`SearchVector` is removed after layer A exists and a third GPU gate run** confirms hybrid still beats lexical in both cultures. Hybrid then answers everywhere, and a vector layer that is *not configured* stops counting as a degradation. |
| D3 | **`SearchRerank` stops being a flag and becomes a deployment capability**: reranking happens when a reranker is registered, which the AppHost does only on a GPU; elsewhere a named `NoReranker` stands in and is not a degradation. |
| D4 | **The five refuted knobs are deleted** — `MinimumSimilarity`, `VectorWeight`, `MeaningFirstWithoutFullMatch`, `MinimumRerankScore`, `NoAnswerBelowRerankScore` — with their tests. `ILexicalFullMatch` stays, as layer A's signal. |
| D5 | Two scale decisions taken now: layer A's signal reads **what a product is**, not its description; layer B's margin is bound to a **fingerprint of the taxonomy and the anchor file** and switches itself off when that changes. |
| D6 | A **trained query classifier** is not built now. It is a backlog row with an entry condition, and an article outline; layer B is the baseline it would have to beat. |

## Layer A — an honest answer, no model

### Contract

`SearchResultPage` gains `SearchAnswer? Answer`:

```csharp
public sealed record SearchAnswer(SearchMatch Match, string? HelpTopic, string? OutOfScopeLabel = null);
public enum SearchMatch { Exact, Related, OutOfScope }
```

- `Exact` — at least one active product (in the requested category branch, if any)
  carries **every** word of the query in the fields that say what a product is.
- `Related` — none does; the results are the closest things the shop sells, not matches.
- `OutOfScope` — only layer B sets it (see below).
- `Answer` is `null` for a browse (no text) and for a date sort: there is nothing to check.
- Both strategies compute it. Lexical search is the permanent fallback, and it has to be
  honest too. The count is one Elasticsearch `_count`; if it fails, lexical search is
  failing as well, so no new degradation path is introduced.
- The public wire shape is a flat object with string enums, added to
  `docs/openapi/tendero.json`, and the frontend types are regenerated.

### The full-match signal (D5)

`ElasticsearchFullMatch` today counts products carrying all words across **every**
searchable field, **with fuzziness**. At a hundred products that is harmless; at a
million, some description will contain the words of almost any query ("ideal para
regalar a tu madre") and the notice would vanish exactly where it is needed. The
signal changes to: all words, **no fuzziness**, over `name`, `brand`, `categoryPathText`
and `attributesText` only. Ranking keeps using the description; only the notice's
question changes. The two existing clause builders stay for search; the full match gets
its own, and a request-body test (`ElasticsearchRequestBodyTests` style) holds the field
list and the absence of fuzziness.

### Help topics

A closed, committed list of non-product intents, each with phrases in es and en,
matched deterministically: the query is lower-cased, accents folded, and a topic
matches when one of its phrases appears as whole terms. **A topic exists only if its
destination exists** (article 07's rule applied to content): today that is one,
`returns` → `GET /api/returns/policy` (phrases such as "devoluciones", "devolver",
"política de devolución", "returns", "return policy", "send back"). Shipping and
contact wait for a page of their own. The list lives in `src/Search` as data with a
test that every topic has phrases in both cultures and a known destination.

The help topic does **not** redirect: Baymard's pattern for vague terms is a help
result shown above products, so the word keeps finding products too.

### What the shopper sees (storefront)

- `Exact`: unchanged.
- `Related`: a visible notice — *"Nothing matches «regalo abuela» exactly"* /
  *"No tenemos nada que coincida exactamente con «regalo abuela»"* — and the results
  under the heading *"The closest things we sell"* / *"Lo más parecido que vendemos"*.
  **Nothing is cut**: "regalo abuela"'s products are good ones.
- `OutOfScope`: *"We don't sell {label}"* / *"No vendemos {label}"*, the departments,
  and at most four products under *"Other things we do sell"* / *"Otras cosas que sí vendemos"*.
- `HelpTopic`: a help card above the products linking to the policy page, with its own
  styling (not a product card).
- Every string in both languages through Transloco; no clay beyond the page's one
  primary action (`design/DESIGN.md`).

### Agents

- The shop's own MCP tools (`search_products`) return `answer` as part of the page.
- UCP's catalogue bindings (MCP and REST) add an informational `messages` entry for
  `Related`, `OutOfScope` and a help topic, and every response is validated against the
  vendored UCP schemas as today.

### Evaluation

- **`admitted`** — for no-answer queries (`not-sold`, `non-product`): the share whose
  page says so (`Related` or `OutOfScope`, or a help topic for `non-product`). Higher is
  better. It replaces `shown@10` as the headline for that group; `shown@10` stays as a
  diagnostic.
- **notice rate** — for the 52 scored queries per culture: the share answered `Related`.
  Reported, not gated; it says how often a query with real answers is told "nothing
  matches exactly".
- **false out-of-scope** — for the scored queries: the count answered `OutOfScope`.
  Must be zero (layer B's acceptance).
- The CI gate keeps running lexical; NDCG thresholds are unaffected, because layer A
  and a passing layer B change no ranking.

## Layer B — a query-scope classifier by anchors

### Decision rule

For a query with text, with the query embedding the vector side already computes
(reused, not recomputed):

- **inside anchors**: every catalogue category (23 today, three levels), each as its
  localized name and its localized path ("Hogar › Cocina › Menaje de cocina"), read
  from `Category` through a port;
- **outside anchors**: a committed file of about thirty categories the shop does not
  sell, in es and en, written by us (phones, toys, books, plants, car accessories,
  groceries, cosmetics, pet supplies, furniture, …). Written before measuring; no
  third-party taxonomy file, so no licence question;
- `s_in` = highest cosine similarity to an inside anchor, `s_out` = highest to an
  outside anchor, margin `M`:
  - `s_out − s_in ≥ M` → **out of scope**, carrying that outside anchor's label;
  - `s_in − s_out ≥ M` → **in scope**;
  - otherwise → **ambiguous**.
- Only *out of scope* changes the page (`SearchMatch.OutOfScope`). *In scope* and
  *ambiguous* leave layer A's answer as it is. The ambiguous band is what protects gift,
  use-case and symptom queries, which name no product type.

The model picks an anchor from a closed set; code writes the sentence (invariant 12).
Anchor vectors are computed once per process and cached; the extra cost per query is
about a hundred dot products.

### Degradation and scale guard

- No embedding generator, or it fails or is past the vector budget → no classification;
  the page is layer A's answer. A test asserts that with a throwing generator the answer
  equals layer A's, and that it is not recorded as a degradation when the layer is simply
  not configured.
- **Fingerprint (D5)**: the committed margin is stored with the number of inside anchors,
  a hash of the category codes and labels, and a hash of the outside-anchor file. When the
  running taxonomy or file does not match, layer B turns itself off, logs once that it
  needs re-measuring, and the shop keeps layer A. A margin chosen for 23 categories cannot
  silently judge a taxonomy of four hundred.
- **Exit condition** (ADR 0032): past a few hundred categories, or once interaction logs
  exist, anchors give way to a trained classifier (D6).

### No flag

Layer B does not sit behind a `FeatureId`. It is measured by name in `SearchEval`; if it
passes, it runs wherever the vector layer runs; if it fails, its code is deleted, like the
knobs in D4.

### Protocol — declared in the notebook before anything is measured

1. **New scope queries**, because `tune` and `holdout` have been looked at many times:
   about 40 per culture, phrased after real shopper phrasings in ESCI, labelled only
   `in` or `out` (no product judgments). The `in` half deliberately includes the hard
   cases: gifts, symptoms, use cases, negations. Split into `scope-dev` and `scope-test`
   before measuring, pairs kept together across cultures.
2. **Margin chosen on `scope-dev`** from a grid declared in advance: 0.02, 0.05, 0.08, 0.12.
   Rule: the value with the most `out` queries recognised among those with zero `in`
   queries marked out of scope; ties to the larger margin.
3. **Acceptance, run once**, on `scope-test` plus the 52 scored golden queries per
   culture:
   - **zero** in-scope queries answered out of scope, in both cultures — non-negotiable;
   - at least half of the `out` queries in `scope-test` recognised, per culture;
   - the eight original no-answer queries reported apart, as secondary evidence.
4. If it fails, layer B is refuted, its code deleted, and the result published anyway.

## `SearchVector` and `SearchRerank` (D2, D3)

- **"Not configured" is not "degraded".** Today a machine without the vector layer would
  count every hybrid search in `tendero.search.degraded`. The strategy distinguishes the
  named stand-ins (`NoVectorLayer`, new `NoReranker`) from a layer that is configured and
  failing: stand-ins skip the stage silently (a span tag `search.stage.unavailable`, no
  counter, no warning); a failure keeps today's tag, counter and log.
- `SearchRerank` is deleted from `FeatureId`, from the AppHost's environment and from the
  strategy; reranking happens exactly when `IReranker` is a real reranker. The AppHost
  keeps starting the reranker only on a GPU.
- **Third gate run** (GPU, throwaway Elasticsearch 9.5.0, Qdrant v1.18.0, TEI at the
  pinned commit), after layer A lands. If hybrid clears its thresholds and beats lexical in
  both cultures — three consecutive runs — `SearchVector` is deleted: `SearchProductsHandler`
  asks the hybrid strategy always, the AppHost stops writing the flag, and the enum is left
  with no members until phase 11 or 12 brings one. If it does not, the flag stays and the
  run is recorded.

## Testing

- Unit: the full-match request body (fields, no fuzziness); `SearchAnswer` for both
  strategies (exact, related, browse, date sort); help-topic matching (accents, whole
  terms, both cultures, destination exists); layer B's three outcomes and the margin
  boundary with a deterministic fake generator; degradation and not-configured paths for
  vector and reranker; the fingerprint switch.
- Architecture: the existing flag rules still hold with one or zero members. If a rule
  asserts that `FeatureId` is non-empty (to avoid passing vacuously), that assertion is
  not deleted: it is rewritten to say an empty enum is allowed on purpose, and the ADR 0023
  amendment records why — never weakened silently.
- Contract: OpenAPI regenerated (`UPDATE_OPENAPI=1`), frontend types regenerated, UCP
  responses validated against the vendored schemas including the new `messages`.
- Frontend: component tests for the notice, the out-of-scope block and the help card in
  both languages; one Playwright storefront spec for the `Related` notice.
- Evaluation: `admitted`, notice rate and false out-of-scope in `SearchEval`, with
  mutations proving each metric turns a test red.
- Level 1 mutation testing by hand for every new rule, listed in the commit messages;
  Stryker over `Search` (and `Mcp`/`Ucp` if touched) at close, against the 75.97 %
  baseline, survivors classified in `docs/testing.md`.
- Opt-in integration test against the real TEI (`TENDERO_MODEL_TESTS=1`) for anchor
  embedding.

## Order of work

1. This spec and the research reports.
2. Notebook entry: the four decisions, what the research taught, layer B's protocol.
3. Delete the refuted knobs (D4).
4. `SearchRerank` → capability; not-configured ≠ degraded (D3).
5. Layer A: contract, full-match signal, help topics, both strategies, OpenAPI and types,
   storefront, MCP and UCP, evaluation metrics.
6. Layer B: anchors, fingerprint, scope queries, classifier, measurement by the protocol;
   kept or deleted.
7. Third GPU gate run; `SearchVector` deleted if its condition is met (D2).
8. Phase close: Stryker and survivors.

Many commits by theme on `feature/search-answers`, one push, one pull request to `develop`.

## Documentation

- **ADR 0032** (new): *knowing that the shop does not sell something is a
  query-understanding decision, not a threshold on a retrieval score* — the evidence of
  H1, H4 and H5, the research, the two layers, the fingerprint and the exit condition.
- **ADR 0023** amended (flags two → one → none); **ADR 0031** amended (reranking is a GPU
  capability, not a flag).
- `docs/search-evaluation.md`: the new metrics, the protocol, the results of layer B and
  of the third run. `docs/architecture.md`: the search section and a new mermaid of the
  search pipeline (lexical → semantic → fusion → reranker → query understanding → answer).
  `docs/analysis/gap-analysis.md`: the hybrid search and faceted-search rows.
  `docs/testing.md`: baselines. `docs/delivery-plan.md`: *Where I left off*, phase 8's row,
  and the backlog.
- **Glossary** (`docs/glossary.md`): *semantic search* as the umbrella (embeddings, vector
  search, reranking) and how it relates to lexical and hybrid; *RAG*, stating plainly that
  Tendero has the retrieval and not the generation today, and where generation arrives
  (phases 10 and 12); *cross-encoder*; *query understanding*.
- **Diagrams**: the four published with article 00 (`docs/blog/assets/01`–`04`) are not
  touched. A new source `docs/blog/assets/07-search-layers.html`, exported to `.svg` and
  `.png`, for article 09.
- **Backlog row (D6)**: *trained query classifier (Q2PT)* — today 23 categories, anchors,
  no logs; enters when the taxonomy passes a few hundred categories, interaction logs exist,
  or layer B is refuted by a failure a trained model could fix; to decide then: the label
  source (ESCI mapped to the taxonomy, LLM distillation reviewed by a person, or logs),
  ML.NET or a head over the existing embeddings, and the licence of everything linked.
  Plus a backlog row for evaluation at scale: a hand-judged golden set stays a regression
  test past a few thousand products, not a measure of absolute quality.
- **Blog** (`docs/blog/index.md`): article 09's piece 4 (*What a shop should say when it
  has nothing*) rewritten with the research, the references and the numbers, and assessed as
  an article of its own; a new outline, *When anchors stop being enough: a query classifier
  trained in .NET*, with the references already gathered (Amazon Q2PT, Instacart,
  Skinner & Kallumadi, CLINC150, DETER).

## Out of scope

- An LLM classifier (it would bring the first `IChatClient` for a decision that needs no
  generation; zero-shot LLMs trail fine-tuned models at out-of-domain detection).
- Query performance prediction, except possibly as a diagnostic column later.
- Relative cutoffs (autocut, top-match-relative): they trim tails and cannot say "nothing".
- Redirects, and help topics without a destination page.
- Changing ranking: no NDCG threshold moves because of this work.
