# Search evaluation

## Golden set

`tools/SearchEval/golden/{culture}.json` — 50–100 curated queries per culture
(60 per culture since 2026-09-15: 52 scored and 8 with nothing to find, against
the 100-product seed):

```json
{
  "culture": "es",
  "source": "seed",
  "queries": [
    {
      "query": "zapatillas running mujer",
      "kind": "feature",
      "split": "tune",
      "pair": "E02",
      "judgments": [
        { "externalId": "B073WXYZ01", "relevance": 3, "why": "exact category + gender match" },
        { "externalId": "B07TUVW404", "relevance": 1, "why": "running shoe, wrong gender" },
        { "externalId": "B08JKLM202", "relevance": 0, "why": "backpack — brand noise" }
      ]
    }
  ]
}
```

Judgments are keyed by **`externalId`**, the id from the source (`item_id` in
the seed), not by the internal `ProductId`: that one is a GUID v7 minted on
every import, so a golden set keyed on it would expire each time the catalogue
is reimported. The source id is stable and readable in a diff.

Cover: easy head queries, synonyms, typos ("zapatilas"), ambiguous queries,
attribute queries ("cafetera 12 tazas"). The `why` field is what makes
annotations reviewable in PRs.

Every query carries three labels, and a test holds all three:

- **`kind`** — one of the eight query types in Baymard Institute's
  [*Ecommerce search query types*](https://baymard.com/blog/ecommerce-search-query-types)
  (`exact`, `product-type`, `feature`, `use-case`, `abbreviation`,
  `compatibility`, `symptom`, `non-product`), plus `not-sold`, which Baymard
  does not name. A report breaks NDCG@10 down by kind, because an overall mean
  hides whether hybrid search wins where nobody names a product. A pair can
  differ in kind between the two cultures when the words do: `camiseta hombre`
  is a feature query, its English half `trail running shirt` a product type.
- **`split`** — `tune` or `holdout`, decided before anything was measured.
  Fusion is adjusted with `--split tune` and checked with `--split holdout`; the
  gate (`--ci`) always measures every query and refuses a split.
- **`pair`** — the same query in the other culture. A pair lands in one split
  whole, so tuning never sees half of a question it is later checked on.

**Queries with nothing to find** (`not-sold`, `non-product`) have no relevant
judgment by construction — `juguetes para niños de 6 años` in a shop that sells
no toys, `devoluciones` in a search over products. They are never scored with
NDCG, because an NDCG over nothing relevant does not exist and a 0 or a 1 would
be invented; they never count as unjudged and never enter a judgment pool. The
report gives them their own section instead: `shown@10`, how many results a
strategy put on the first page anyway, and the first result a shopper would
read. Lower is better. The proposal that added them, with the reasoning per
query, is `tools/SearchEval/proposals/2026-09-15-queries.md`.

## Metrics

- **NDCG@10** — graded ranking quality vs the ideal order (gain discounted by
  log of position, normalized to 0–1).
- **recall@50** — did the relevant items appear at all (guards the retrieval
  stage independently of ranking).

## CI gate

`dotnet run --project tools/SearchEval -- --ci` drops and recreates the indexes,
indexes the seed through the real ports, scores both cultures, and exits
non-zero below the thresholds in `eval.thresholds.json`. Threshold or annotation
changes require justification in the PR body. `--report <file>` writes the
Markdown report for publishing as an artifact.

It does **not** start Elasticsearch itself — it takes `--elasticsearch <url>`
and defaults to `http://localhost:9200`. Booting a container from the tool would
mean a Testcontainers dependency for something a service container already does.

**Point it at a throwaway engine, never at a running stack's.** The tool drops
the indexes on the way in, and it mints fresh product ids per run — so its corpus
and the application's occupy the same index under different ids, and neither
sweep can see the other's documents. Running it against the Aspire container left
six orphans per culture claiming `inStock: false` for products that were in
stock, which reads exactly like an application bug and is not one.

Two things make the run reproducible, and both were learned the hard way:

- **Drop the indexes first.** Product ids are GUID v7 minted per run, so without
  it the previous run's documents stay and compete in the ranking. Two runs of
  identical code scored 0.674 and then 0.360.
- **Refresh explicitly after indexing.** Elasticsearch refreshes once a second,
  so otherwise the score depends on whether the refresh landed before or after a
  query: 0.860 and then 0.769. Polling until *some* query returns a hit is not
  enough — it only proves one document is visible, not all six.

## Hybrid search, measured: it does not beat lexical in Spanish (2026-09-15)

> **Superseded the same night.** Every hybrid figure in this section and the
> three that follow it was taken with a card reader that dropped meaning's
> products — see *The filter the client swallowed*. With it fixed, hybrid beats
> lexical in both cultures. The sections are kept as they were written, because
> how the mistake was found is part of the record.

**The first measurement of hybrid search, and the answer is mixed.** Both
strategies over the same corpus — the 100-product seed and the 450 judgments —
with `--strategy hybrid`, against throwaway engines: Elasticsearch 9.5.0,
Qdrant v1.18.0, and Text Embeddings Inference at commit `4150561` serving
`bge-m3@5617a9f` and `bge-reranker-v2-m3@953dc6f` on the GPU. Two runs of the
first configuration gave identical numbers.

| | lexical | hybrid (RRF) | hybrid + reranker | hybrid, `VectorDepth` 10 |
|---|---:|---:|---:|---:|
| es NDCG@10 | 0.927 | **0.912** | 0.924 | 0.891 |
| es recall@50 | 0.939 | 0.945 | 0.945 | 0.939 |
| en NDCG@10 | 0.935 | **0.944** | 0.912 | 0.940 |
| en recall@50 | 0.955 | 0.955 | 0.955 | 0.955 |
| mean ms per query | 12–24 | 29–35 | 79–82 | 33–37 |

**English gains and Spanish loses.** Fusion helped the queries whose words
under-describe what they want — `running shoes` 0.820 → 0.979, `reading light
with usb` 0.933 → 0.993 — and hurt queries lexical already answered well:
`camiseta hombre` 1.000 → 0.857, `menaje de cocina` 0.901 → 0.774, and `gift for
the kitchen` 0.867 → 0.738. **`regalo para casa`, the query this file kept for
meaning to earn, moved 0.305 → 0.355**: better, and nowhere near an answer.

**The reranker made it worse**, not better: English falls under its threshold
(0.912), with `gift for the kitchen` at 0.508, and Spanish stays under lexical.
It stays off — which on a CPU it already was, for its latency.

**One tuning experiment, declared before it ran, and it was refuted.** The
hypothesis was that weak nearest neighbours were displacing good lexical hits,
so the vector side contributed ten candidates instead of twenty — a cut that
matches NDCG@10 rather than one read off the judgments. Spanish fell further,
to 0.891, with `menaje de cocina` at 0.462. The weak neighbours are not the
problem, and the defaults stay where they were. **The search stopped there on
purpose**: with 22 queries per culture, trying values until one scores better
is fitting the golden set, not improving search.

**What is committed, and what it means.** Hybrid gets its own thresholds, about
0.013 under what the first configuration measured — es 0.90 / 0.93, en 0.93 /
0.94 — and lexical keeps 0.91 / 0.92 and 0.92 / 0.94, untouched. A hybrid bar
below lexical's in Spanish is not a lowered standard: it is the measurement
written down, so that a change which makes hybrid worse still fails. The CI job
runs lexical alone, because a runner has no model server. **`SearchVector`
stays off by default**: its removal condition is hybrid beating lexical in both
cultures, and today it does not in one.

### The ruler was built by the strategy it examines (2026-09-15)

**Before tuning anything, the measurement itself was measured.** The golden set
was completed on 2026-09-11 from the top ten of three *lexical* query variants,
and a result nobody judged scores as irrelevant. So the report grew `judged@10`
— the share of a strategy's first ten that carries any judgment — and a
vector-only strategy, as a diagnostic that gates nothing:

| | lexical | hybrid | vector only |
|---|---:|---:|---:|
| en judged@10 | 1.000 | 0.838 | 0.400 |
| es judged@10 | 1.000 | 0.850 | 0.386 |
| en NDCG@10 | 0.935 | 0.944 | 0.491 |
| es NDCG@10 | 0.927 | 0.912 | 0.504 |

Lexical is fully covered because the pool was its own. **Fifteen per cent of
hybrid's first ten and sixty per cent of the vector side's have never been
judged**, so both of their NDCG figures are lower bounds, and the vector one
measures almost nothing yet. None of the 2026-09-15 conclusions above can stand
until that is fixed. `SearchEval --pool <file>` writes every strategy's unjudged
first ten — 282 candidates across 42 of the 44 queries — and judging that pool,
proposed with a `why` and reviewed by a person, is the next step.

### The pool, judged, and the baseline re-derived (2026-09-15)

**The 282 were judged blind** — the judge was not told which strategy surfaced a
product — and proposed with a `why` each. The author did not grade them row by
row: he decided the criteria that grade many rows at once, and resolved nine
doubts about existing judgments. Both are recorded, with every change they made,
in `tools/SearchEval/proposals/2026-09-15-pool.md`; the criteria in short:

- For "regalo para casa", kitchen and table textiles, tableware and decorative lamps are good answers (2), technical lamps related (1).
- A product meeting one of the query's two conditions is 1.
- An everyday cotton tee or a linen shirt is 1 for a technical or running shirt query.
- An accessory of the same family that is not what was asked — bamboo utensils for pans, kitchen textiles for "menaje de cocina" — is 0, which lowered existing judgments too.

**242 of the 282 are 0 and none is a 3.** The ruler's bias was real and small:
most of what the vector side adds and nobody had judged is genuinely irrelevant.
With the pool judged, every strategy is fully covered (`judged@10` 1.000), and two
runs gave identical numbers:

| | lexical | hybrid | vector only |
|---|---:|---:|---:|
| es NDCG@10 | 0.921 | 0.916 | 0.529 |
| es recall@50 | 0.930 | 0.939 | 0.457 |
| en NDCG@10 | 0.921 | **0.944** | 0.512 |
| en recall@50 | 0.921 | 0.925 | 0.467 |

- **Hybrid leads in English by 0.023 and trails in Spanish by 0.005**, against +0.009 and −0.015 with the old ruler. The ruler explained part of it; it did not turn the result.
- **Lexical went down** — 0.935 → 0.921 in English — because relevant products it never retrieved now count. The engine did not change.
- **Withdrawn the same night** — see *The filter the client swallowed* below: the card reader had lost its id filter, so the "vector" column and most of the vector side's pool were the cheapest Active products, not meaning's neighbours. With the reader fixed the vector side alone scores 0.80 / 0.77. What follows was written before that was known. **The vector side alone is weak** (0.51 / 0.53) with a complete ruler. Without a similarity floor it fills its twenty places with whatever is nearest, socks for "camiseta hombre", and fusion lets them in. That is the next thing to fix.
- `regalo para casa`: lexical 0.220, hybrid 0.427.

**The thresholds were re-derived, and for the one reason that is allowed**: the
ruler changed, not the engine. The rule is the one this file already states — at
least 0.013 under the measurement, rounded down to two decimals: lexical es 0.90 /
0.91 and en 0.90 / 0.90, hybrid es 0.90 / 0.92 and en 0.93 / 0.91. Lexical's bar
coming down is not room made for hybrid; it is the same engine measured by a
fuller ruler.

The same day a browser found a catalogue defect: `B11COO0301`, the cast iron
pan, carried `material: acero inoxidable`. `MATERIAL` is a closed option set
with no cast iron in it, so a `CAST_IRON` option was added with the fix.
Material is indexed, so the gate was run rather than reasoned about: es 0.927 /
0.939 and en 0.935 / 0.955, unmoved.

### The exam grew to the questions shoppers ask (2026-09-15)

**The golden set covered two of Baymard's eight query types**, the two word
matching already answers, so hybrid search was examined on lexical search's home
ground. It grew to 52 scored queries per culture and 8 with nothing to find,
every one labelled with its type, its split and its counterpart (see *Golden
set* above; the queries and their reasons are in
`tools/SearchEval/proposals/2026-09-15-queries.md`).

The 30 new pairs were judged blind against the **whole catalogue**, then every
strategy's unjudged first ten — 449 products — was judged blind too: 403 at 0,
44 at 1, 2 at 2. Every criterion and decision is in
`tools/SearchEval/proposals/2026-09-15-new-queries.md`. The golden sets hold 942
(es) and 985 (en) judgments, and `judged@10` is 1.000 for all three strategies.
Two runs agreed to the third decimal; hybrid English moved by 0.001 between
them, which is the vector side on a GPU rather than the ruler.

| | lexical | hybrid | vector only |
|---|---:|---:|---:|
| es NDCG@10 | 0.693 | **0.704** | 0.451 |
| es recall@50 | 0.737 | 0.752 | 0.433 |
| en NDCG@10 | 0.749 | **0.774** | 0.444 |
| en recall@50 | 0.800 | 0.810 | 0.445 |

**The engine did not change; the exam did.** The 22 original queries still score
what they scored — lexical 0.921 in both cultures, hybrid 0.917 (es) and 0.944
(en) — and the 30 new ones score lexical 0.525 / 0.623 and hybrid 0.548 / 0.651.
**Hybrid now leads in both cultures**, and by more on the queries nobody had
asked it before.

**Every hybrid and vector figure in this section was taken with the broken card
reader described in the next section**, and under-counts the vector side; the
lexical figures and the thresholds' lexical half stand. The corrected numbers
follow that section.

By query type, NDCG@10 (es | en):

| type | queries | lexical | hybrid | vector only |
|---|---:|---|---|---|
| exact | 3 | 1.000 \| 1.000 | 1.000 \| 1.000 | 0.550 \| 0.550 |
| compatibility | 3 | 0.985 \| 0.990 | 0.988 \| 0.991 | 0.849 \| 0.859 |
| feature | 16 \| 15 | 0.859 \| 0.889 | 0.835 \| 0.889 | 0.434 \| 0.437 |
| abbreviation | 3 | 0.850 \| 0.828 | **0.904 \| 0.919** | 0.219 \| 0.244 |
| product-type | 12 \| 13 | 0.793 \| 0.766 | 0.772 \| 0.804 | 0.471 \| 0.437 |
| symptom | 3 | 0.357 \| 0.573 | 0.409 \| 0.534 | 0.287 \| 0.302 |
| use-case | 12 | 0.264 \| 0.456 | 0.339 \| 0.514 | **0.430** \| 0.415 |

- **Use case is where meaning earns its place, and fusion throws half of it away.** In Spanish the vector side alone scores 0.430 against lexical's 0.264, and the fused list only 0.339.
- **Symptom queries fail for every strategy** — "algo para no mojarme en la bici", "sarten que no se pegue".
- **Hybrid still costs Spanish a little where lexical is strong** (feature 0.859 → 0.835, product type 0.793 → 0.772).

**On the queries with nothing to find, the vector side always answers.** Mean
`shown@10`: lexical 1.4 (es) and 2.6 (en), hybrid 5.1 and 5.3, the vector side
10.0 in both — ten products on the first page for "móvil para mi madre de 70
años" in a shop with no phones. That is the missing similarity floor, measured.

**The thresholds were re-derived by the same rule** — at least 0.013 under the
lower of the two runs, rounded down to two decimals: lexical es 0.68 / 0.72 and
en 0.73 / 0.78, hybrid es 0.69 / 0.73 and en 0.76 / 0.79. The bar is lower
because the exam now asks what shoppers ask, not because anything got worse; the
22 queries that set the old bar still clear it.

### The filter the client swallowed (2026-09-15)

**Three tuning hypotheses were tested on this exam and all three failed, and the
third failed in a way that did not add up.** Declared before running, each on the
`tune` split and checked once on `holdout`: a similarity floor on the vector side
(H1), a weight on the vector ranking in the fusion (H2), and answering in meaning's
order when no product carries every word of the query (H3). H3's signal worked —
a direct count gave zero full matches for "regalo abuela" — and yet the hybrid
answer to "regalo abuela", whose lexical list is empty, scored 0.081 while the
vector side alone scored 0.452. With nothing to fuse they had to be identical.

A diagnostic run with fifty vector neighbours instead of twenty took "regalo
abuela" to 0.452 and Spanish use-case queries from 0.312 to 0.532: the more cards
hybrid search asked for, the more of meaning's products survived. That is not
relevance. The request bodies the .NET client really wrote, captured against the
evaluation engine, showed why:

```
three .Filter calls:           {"query":{"bool":{"filter":{"term":{"status":{"value":"active"}}}}},"size":3}
two .Term calls in one filter: {"query":{"bool":{"filter":{"term":{"categoryCodes":{"value":"KITCHEN"}}}}},"size":3}
```

**In `Elastic.Clients.Elasticsearch` a second `.Filter(...)` on a bool query, or a
second `.Term(...)` on one query descriptor, replaces the first.** It compiles and
says nothing. Two defects followed from it, both there since the code was written:

- **The card reader hybrid search uses lost its id filter.** Asked for the cards of the products meaning found, it answered with the cheapest Active products, and hybrid search dropped every meaning-found product that was not among them by luck. Every hybrid number in this file before this section under-counts the vector side; the vector-only diagnostic did better because it asked for fifty cards out of a hundred products.
- **A lexical search inside a category lost `status: active`**, so browsing a category could show products that were not published — in the shop, not only in the evaluation.

`ElasticsearchRequestBodyTests` now reads the body the client writes (against a
node nobody listens on: the call fails after the body is written, and only the
body is asserted). Its three tests failed against the unfixed code for exactly
this reason, and pass with every filter passed to one `.Filter(...)` call.
**H1, H2 and H3 were measured against the broken reader, so none of the three
counts as a refutation**; they are repeated below, knowing that `holdout` has
already been looked at once.

**What the fix changed.** Meaning's products finally reached the first ten, and
177 of them had never been judged, because none had ever arrived. They were
judged blind (143 at 0, 25 at 1, 9 at 2; `tools/SearchEval/proposals/2026-09-15-new-queries.md`,
*Fourth pass*), and with every strategy's first ten judged again (`judged@10`
1.000), es 1053 and en 1075 judgments:

| NDCG@10, en / es | lexical | vector only | hybrid | hybrid + reranker |
|---|---:|---:|---:|---:|
| all 52 queries | 0.748 / 0.692 | 0.812 / 0.773 | 0.810 / 0.783 | **0.843 / 0.831** |
| `holdout` | 0.744 / 0.729 | 0.776 / 0.786 | 0.795 / 0.794 | **0.837 / 0.856** |
| use case | 0.455 / 0.263 | 0.659 / 0.619 | 0.610 / 0.619 | 0.672 / 0.613 |
| symptom | 0.573 / 0.357 | 0.603 / 0.503 | 0.545 / 0.460 | 0.704 / 0.646 |
| feature | 0.889 / 0.859 | 0.865 / 0.832 | 0.889 / 0.835 | 0.915 / 0.920 |
| product type | 0.765 / 0.793 | 0.824 / 0.777 | 0.851 / 0.813 | 0.845 / 0.869 |

- **The vector side is not weak; the reader was.** Alone it beats lexical in both cultures, and hybrid is roughly the vector side with lexical's precision on feature queries kept.
- **Use case in Spanish goes from 0.263 to 0.619.** That is the gap this file said hybrid search had to earn, earned.
- **The reranker adds 0.033 (en) and 0.048 (es)**, most on symptom and feature queries — the ones that need the whole sentence read — and holds on `holdout`, where nothing was tuned: 0.837 / 0.856.
- **The queries with nothing to find are worse, and now really so.** Hybrid shows ten products on the first page for all eight, against lexical's 1.4 (es) and 2.6 (en).

**The three hypotheses, repeated with the reader fixed**, same declared values
and rules, `tune` then one `holdout` run (hybrid NDCG@10, en + es):

| | `tune` | `holdout` | verdict |
|---|---:|---:|---|
| reference (no floor, weight 1, no routing) | 0.825 + 0.773 | 0.795 + 0.794 | — |
| H1 similarity floor, 0.35 chosen of five | 0.823 + 0.773 | 0.795 + 0.794, `shown@10` still 10.0 | refuted |
| H2 vector weight, 1.5 chosen of four | 0.837 + 0.770 | 0.798 + 0.791 | refuted (+0.000) |
| H3 meaning first without a full match | 0.845 + 0.778 | 0.794 + 0.794 | refuted |

- **None of the three generalised.** H3's +0.025 on `tune` was noise with a good face — which is what 28 queries per culture can produce and what `holdout` exists to catch. The defaults stay: no floor, equal weights, no routing. The knobs stay too, at those values, each with the mutations its tests catch.
- **A floor trades one failure for another.** On `tune` every step up empties a page on the queries with nothing to find and takes relevance off the use-case queries (es 0.636 → 0.456 at 0.45): the cosine of a neighbour that does not belong and of one that helps fall in the same band. Showing nothing when nothing fits needs a different signal — the reranker reads the whole sentence and is the natural candidate — and is the open question this section leaves.
- **What improved search was not tuning**: an exam that asks what shoppers ask, a fixed defect, and the reranker, which takes hybrid on `holdout` from 0.795 / 0.794 to 0.837 / 0.856 without having been tuned on anything. It is a GPU feature (ADR 0031) and stays behind `SearchRerank`.

**H4: letting the reranker say "not an answer" (2026-09-16).** Declared before it
ran: a result the reranker scores below T is not shown, and once any of its
twenty-result head is turned away, nothing past the head is either. Five values on
`tune`, hybrid with the reranker (reference 0.849 + 0.810 = 1.659, `shown@10`
10.0 / 10.0), the rule being to take the lowest `shown@10` among values costing at
most 0.010 of NDCG:

| T | en | es | sum | `shown@10` en / es |
|---|---:|---:|---:|---:|
| 0.01 | 0.737 | 0.721 | 1.458 | 0.0 / 0.8 |
| 0.03 | 0.710 | 0.677 | 1.387 | 0.0 / 0.0 |
| 0.1 | 0.660 | 0.570 | 1.230 | 0.0 / 0.0 |
| 0.3 | 0.526 | 0.530 | 1.056 | 0.0 / 0.0 |
| 0.5 | 0.511 | 0.515 | 1.026 | 0.0 / 0.0 |

It empties the page for "móvil para mi madre de 70 años" from the lowest value
up, and costs every other query 0.2 of NDCG and recall@50 from 0.939 to 0.666. No
value qualified, so **H4 is refuted on `tune`** and nothing went to `holdout`. The
rule was the problem: any head of twenty holds something the reranker scores low,
so one rejection took the whole tail of every query, and the reranker's sigmoid
puts products a shopper would accept below 0.01. The queries with nothing to find
remain the open question.

**H5: deciding per query instead (2026-09-16).** Declared before it ran, same five
values and the same selection and acceptance rules: when not even the head's best
result reaches T, the query has no answer and the page is empty; when one does,
nothing is cut.

| T | en | es | sum | `shown@10` en / es |
|---|---:|---:|---:|---:|
| 0.01 | 0.802 | 0.783 | 1.585 | 0.0 / 2.5 |
| 0.03 | 0.802 | 0.757 | 1.559 | 0.0 / 0.0 |
| 0.1 | 0.743 | 0.638 | 1.381 | 0.0 / 0.0 |
| 0.3 | 0.641 | 0.585 | 1.226 | 0.0 / 0.0 |
| 0.5 | 0.583 | 0.585 | 1.168 | 0.0 / 0.0 |

It costs a third of what H4 did at the same T and still nowhere near the 0.010
allowed, so **H5 is refuted on `tune`** too. The queries it empties at T = 0.01 are
the finding: "organising the kitchen", "staying dry on the bike", "gift for grandma",
"picar verduras" and "regalo abuela" — every one a use-case or symptom query with a
real answer. The reranker orders their products well, which is why it raises NDCG,
and scores even the best of them as low as a lamp for "móvil para mi madre de 70
años". **Its absolute score measures literal closeness, not whether the shop has
something for what was asked**, on exactly the queries where meaning earned its
place. A signal for "nothing here" will have to be relative, or come from somewhere
else — a design question, left open rather than tuned against a split that has
already been looked at many times.

**A reranker on a CPU, measured.** `Alibaba-NLP/gte-multilingual-reranker-base`
at `8215cf0` (Apache-2.0, Spanish included, a third of `bge-reranker-v2-m3`'s
compute) on TEI's CPU image, which has no ONNX file to use at that revision and
falls back to candle at four pairs a batch: twenty real product texts took **35 s
at the median and 39 s at p95**, against a budget of 800 ms. Its quality was not
measured, because no quality pays for that.

**The same model through ONNX Runtime**, from the INT8 export
`onnx-community/gte-multilingual-reranker-base` at `ee64367e` (model file SHA-256
`ccf51dba…7c35`; the export declares no licence of its own, and names the Apache-2.0
base model): TEI takes its ORT backend, eight pairs a pass, and twenty pairs come
back in ~1.0–1.5 s at the median and ~1.5–2.3 s at p95 — **thirty-five times faster
than candle**, still over budget; ten pairs, 535–709 ms at the median. Its quality
was measured in three configurations declared before running (NDCG@10, en / es):

| | hybrid | ms per query | `judged@10` |
|---|---:|---:|---:|
| no reranker | 0.810 / 0.783 | ~27 | 1.000 |
| `bge-reranker-v2-m3` on a GPU | 0.843 / 0.831 | ~80 | 1.000 |
| INT8 on CPU, head of 20, 30 s budget | 0.815 / 0.792 | 699 / 1113 | 0.973 / 0.952 |
| INT8 on CPU, head of 10, 30 s budget | 0.808 / 0.799 | 445 / 731 | 1.000 |
| **INT8 on CPU, head of 10, the real 800 ms budget** | **0.809 / 0.788** | 381 / 458 | 1.000 |

The rule was that the last row beat hybrid without a reranker in both cultures; it
gains 0.005 in Spanish and loses 0.001 in English, so **it does not**. The small
model is the limit as much as the CPU: unconstrained it adds 0.005 / 0.009 against
the GPU reranker's 0.033 / 0.048. **Reranking stays a GPU feature**, now on two
measurements rather than one.

## The lexical fallback, and a golden set with nothing left unjudged (2026-09-11)

**The recall this file blamed on vocabulary was mostly the query's `AND`.** A UI
review asked why the numbers below looked low, and the answer was in the hits
column: every clause of the query demanded every word, so "tech tee" returned
the one product carrying both and missed the four others a shopper would take;
"running shoes" returned 2 of 8. That is not a gap only embeddings can bridge —
it is one clause. A third `should`, `cross_fields` with `operator: or`, retrieves
what the AND drops, and the AND clause is boosted ×2 so a product carrying every
word still ranks first.

Measuring that honestly needed the golden set first. Loosening a query surfaces
products nobody had annotated, and an unjudged product counts as irrelevant, so
the looser query would have been punished for finding things. **The pool was
completed**: the top ten of three query variants (the AND query, AND plus a 75 %
minimum-should-match, AND plus the OR fallback), for every query in both
cultures, was annotated until nothing in any of them was unjudged — 182 new
judgments, 268 → 450. The two brand queries had a hole of their own: "Kuisina"
annotated 12 of the brand's 23 products and "Vertex" 12 of 15, which is why
"Kuisina" scored 0.801 with the engine ranking nothing but Kuisina on top.

| on the completed golden set | es NDCG@10 | es recall@50 | en NDCG@10 | en recall@50 |
|---|---:|---:|---:|---:|
| AND only (what shipped) | 0.800 | 0.564 | 0.739 | 0.457 |
| AND + 75 % minimum should match | 0.832 | 0.654 | 0.812 | 0.618 |
| **AND ×2 + OR fallback** | **0.927** | **0.939** | **0.935** | **0.955** |

The first row is lower than the 0.813 / 0.766 committed before, and that is the
ruler becoming fairer rather than the engine becoming worse: the same results,
judged against more of what was relevant. `gift for the kitchen` — the query
this file kept for hybrid search to earn — went from 0.000 to 0.867 lexically,
because "kitchen" alone now reaches the kitchen.

**What it costs**, stated rather than discovered: result lists are longer ("running
shoes" returns 20 products instead of 2), which is what a shop's search is
expected to do and what the ranking has to keep in order. **What it means for
phase 8**: hybrid search is measured against 0.927 / 0.935 now, not against an
`AND`. Earning the difference is harder, and it is the difference that was
honest all along.

The Spanish catalogue gained its accents the same day — the seed had been
written without a single tilde or eñe in 200 fields — and the gate did not move
by a thousandth: the `spanish` analyzer's light stemmer folds accented vowels,
so "lampara escritorio" still finds "Lámpara de escritorio".

## The baseline was re-derived when the catalogue reached 100 products (2026-09-04)

**The old numbers are not comparable, and lowering the thresholds is not a
regression being waved through.** The corpus went from 6 products to 100 and the
golden set from 30 judgments to 268. NDCG@10 over six documents is a different
measurement from NDCG@10 over a hundred: with six, almost everything retrieved
is annotated, and the metric mostly asks whether the order is right.

| | 6 products | 100 products |
|---|---:|---:|
| es NDCG@10 | 0.943 | **0.819** |
| es recall@50 | 0.909 | **0.642** |
| en NDCG@10 | 0.937 | **0.766** |
| en recall@50 | 0.886 | **0.556** |

What happened in two steps, and the order matters because the first number was
the diagnosis:

**Adding the products alone gave es 0.787 / en 0.844 with recall UNCHANGED** at
0.909 and 0.886. Retrieval still found exactly what it found before; only the
ranking fell, because ninety-four unannotated products were competing for the top
ten and an unannotated product counts as irrelevant. That is a golden set that
has gone out of date, not an engine that got worse.

**Annotating them dropped recall to 0.642 / 0.556**, and that is the honest half.
The new judgments include products a shopper would accept seeing — a gym trainer
under "zapatillas running", a duffel under "mochila" — that a lexical `AND` query
cannot retrieve, because they do not contain the query's words. Recall now
measures that gap instead of hiding it.

**The gap is the number hybrid search has to earn**, and it is finally worth
measuring: it used to be one query per culture scoring 0.000, which is a rounding
error. It is now roughly forty per cent of the annotated set.

One catalogue defect the exercise found, which is the kind only a query can find:
the two new running shoes were called *"de trail"* and *"de competicion"* and the
word **running** appeared nowhere in their Spanish. A shop that sells running
shoes and never writes the word is invisible to the search its own customers
type.

## Committed baseline

Measured against the seed sample, lexical BM25 only. Hybrid's own thresholds are
under the table, and how they got there is in the 2026-09-15 sections above.

| culture | NDCG@10 | recall@50 | threshold NDCG | threshold recall |
|---|---:|---:|---:|---:|
| es | 0.692 | 0.738 | 0.67 | 0.72 |
| en | 0.748 | 0.795 | 0.73 | 0.78 |

Measured against 100 products and 52 scored queries per culture — 1053 (es) and
1075 (en) judgments, every strategy's first ten judged after the card-reader fix —
on 2026-09-15, identical across two runs. Hybrid's own thresholds, from the same
two runs (0.810 / 0.934 en, 0.783 / 0.928 es): **es 0.77 / 0.91, en 0.79 / 0.92**,
above lexical's in both cultures for the first time. The 942 / 985-judgment
figures of the section *The exam grew* were taken before the fix. They replaced 0.921 / 0.921, measured over the 22 queries that
covered only the query types word matching answers, and still what those 22
score. Those had replaced 0.927 / 0.935, measured
against 450 judgments pooled from lexical alone — not comparable, for the reason
the section "The ruler was built by the strategy it examines" gives — which had
replaced 0.813 / 0.766, and 0.943 / 0.937 before the corpus grew.

**The margin is deliberate and it was nearly lost.** The first thresholds sat
0.01 under the measurement, and then six product colours changed to match the
illustrations that arrived for them — colour is indexed, so Spanish moved to
0.813 and the margin fell to 0.003. That is not a gate any more, it is a coin
toss on the next harmless edit. They are back at roughly 0.013, which is what
the original 0.93 / 0.90 pair had — and the 2026-09-11 thresholds keep 0.015
to 0.019.

### What localized attribute values bought (2026-09-02)

The first gap below, closed. Attribute values became typed and localized: the
catalogue stores the option code `NAVY_BLUE`, and the index renders it as "azul
marino" in `products_es` and "navy blue" in `products_en`.

| | before | after |
|---|---:|---:|
| en NDCG@10 | 0.720 | **0.811** |
| en recall@50 | 0.682 | **0.773** |
| es NDCG@10 | 0.860 | 0.860 |
| es recall@50 | 0.841 | 0.841 |

**Spanish did not move, and that was the test.** The change only alters how the
searchable text is rendered; if the Spanish numbers had shifted, the rendering
would have changed something it had no business changing. English thresholds
raised to 0.80 / 0.76, keeping roughly the same margin the previous ones had.

Two queries went from 0.000 to 1.000: `navy blue shoes` and `womens running
shoes` — the two the gap below named. `induction cookware` did **not** move, and
that is the diagnosis holding: the pan now indexes "induction" from its boolean
attribute, but the query needs both terms and "cookware" still lives in a
category code nobody types.

### What the localized taxonomy bought (2026-09-02)

`Product.Category` was the string `COOKWARE`. The index carried a code nobody
types, which is why the field had been taken *out* of the searchable fields —
mapped as `keyword`, it promised a match that could never happen. Categories are
now an aggregate with a localized name and a materialized path, and the document
carries two fields instead of one: `categoryCode` stays a keyword for filters,
`categoryPathText` is analysed text with the whole branch — "Hogar Cocina Menaje
de cocina" / "Home Kitchen Cookware".

| | before | after |
|---|---:|---:|
| es NDCG@10 | 0.860 | **0.943** |
| es recall@50 | 0.841 | **0.909** |
| en NDCG@10 | 0.811 | **0.937** |
| en recall@50 | 0.773 | **0.886** |

**Both cultures moved this time, and that is the expected shape.** The attribute
change only added English text, so Spanish holding still was the test. This one
adds text in both, so Spanish moving is the confirmation rather than the alarm.

The whole branch is indexed, not just the leaf, because someone searching
"cocina" expects to find what is inside it. That is what carried `menaje de
cocina`, `ropa deportiva`, `sportswear`, `induction cookware` and `reading light
with usb` from 0.000 to a hit.

**One query per culture still scores 0.000**, and they are the same query:
`gift for the kitchen` and `regalo para casa`. No amount of taxonomy reaches
them — nothing in the catalogue contains the word "gift". They are the
semantic-intent gap, and they are the number hybrid search has to earn.

### What the first committed baseline bought

The gate's first job was to justify a change to the query itself:

| | before | after |
|---|---:|---:|
| es NDCG@10 | 0.769 | **0.860** |
| es recall@50 | 0.750 | **0.841** |
| en NDCG@10 | 0.674 | **0.720** |
| en recall@50 | 0.636 | **0.682** |

`best_fields` with `operator: and` requires every term in *one* field, so
"zapatillas running mujer" matched nothing: the first two terms are in `name`,
`mujer` is in `attributesText`. The query is now a `should` of two clauses —
`cross_fields` (terms may spread across fields) and `best_fields` with
`fuzziness: AUTO` — because `cross_fields` does not support fuzziness and typo
tolerance had to survive. It did: "zapatilas" and "runing shoes" still score
1.000. Three queries went from 0.000 to 1.000.

`category` also left the searchable fields. It is mapped as a `keyword`, so
listing it among text fields promised a match that could never happen.

### What phase 4 did not move (2026-09-03)

Inventory adds `inStock` to the document and **nothing to the query**, so the
expected result was no result: es 0.943 / 0.909 and en 0.937 / 0.886, identical
across runs. The score moving would have been the alarm.

The run did surface something else. `SearchEval` composes its own container, the
indexer had grown an `IAvailabilityReader` dependency, and the gate had been
throwing on startup since the day that landed — with nothing to notice, because
nothing runs it locally between phases. It is registered now as a deliberately
named `NoInventory` fake: this corpus has no warehouses, and the day a filter
puts `inStock` in the query, that type is what will make the omission obvious.

## Known gaps the baseline still exposes

The queries scoring 0.000 are diagnosis, not noise:

1. ~~**Attribute values are never translated.**~~ **Closed 2026-09-02** — see
   the before/after above. Colour and gender are option codes now, with a label
   per culture, and the two queries this cost went from 0.000 to 1.000.
2. ~~**The category taxonomy is not user vocabulary.**~~ **Closed 2026-09-02** —
   categories carry a localized name and the index carries the whole branch as
   analysed text. It took `induction cookware`, `menaje de cocina`, `ropa
   deportiva`, `sportswear` and `reading light with usb` off this list.
3. **Vocabulary gaps lexical search cannot bridge** — narrower than this said.
   `gift for the kitchen` reaches 0.867 since the OR fallback (2026-09-11):
   "gift" still matches nothing, and "kitchen" alone reaches the kitchen.
   **`regalo para casa` is the one left, at 0.305**: the catalogue files home
   goods under "Hogar", nobody writes "casa" in a product, and the only hit is
   a pair of house slippers. That is the semantic-intent gap, and it is the
   number hybrid search has to earn — against a lexical baseline of 0.927 /
   0.935 rather than the 0.813 / 0.766 an `AND` produced. **First measurement
   (2026-09-15): 0.355 with fusion, 0.348 reranked.** With the pool judged, lexical
   scores 0.220 and hybrid 0.427 — meaning finds the kitchen linen and the
   tableware a gift-giver would take, and it is still far from an answer.

A cautionary note on annotating: "flexo con usb" was originally annotated as a
synonym gap lexical search could not solve. It scores 1.000, and always did —
the Spanish description literally begins "Flexo LED". The annotation was written
from assumption instead of from the catalogue. That is precisely what the `why`
field exists to make visible in review.
