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
report gives them their own section instead. Its headline since 2026-09-17 is
`admitted`, whether the page says nothing matches (see *Metrics*). `shown@10` —
how many results a strategy put on the first page anyway, lower is better — and
the first result a shopper would read stay beside it as diagnostics. The proposal
that added them, with the reasoning per query, is
`tools/SearchEval/proposals/2026-09-15-queries.md`.

## Metrics

- **NDCG@10** — graded ranking quality vs the ideal order (gain discounted by
  log of position, normalized to 0–1).
- **recall@50** — did the relevant items appear at all (guards the retrieval
  stage independently of ranking).

Two more since 2026-09-17 measure what the page **says**, not what it ranks
(ADR 0032). They are reported and never gated:

- **admitted** — for the queries with nothing to find: the share whose page admits
  it — answered `Related`, or, for a `non-product` query, given a help topic.
  Higher is better.
- **notice rate** — for the 52 scored queries per culture, which do have answers:
  the share told that nothing matches exactly (`Related`).

There were three. **false out-of-scope** counted the scored queries told the shop
does not sell them at all, and it had to be zero; only layer B could answer
`OutOfScope`, so it was zero by construction while layer B was off, and it left
the report with the classifier (ADR 0032, amended).

## CI gate

`dotnet run --project tools/SearchEval -- --ci` drops and recreates the indexes,
indexes the seed through the real ports, scores both cultures, and exits
non-zero below the thresholds in `eval.thresholds.json`. Threshold or annotation
changes require justification in the PR body. `--report <file>` writes the
Markdown report for publishing as an artifact.

**The thresholds name the judgments they were set against** (2026-10-24, a
reader's suggestion under article 03). If a pull request changes the code and the
golden set together, the two runs are not scored against the same expectations,
and a moved score cannot tell which of the two moved it. So:

- `eval.thresholds.json` carries `"goldenSet"`, a 12-character fingerprint of the
  golden set (`GoldenSet.Fingerprint`). It is taken over what the gate scores —
  each culture and source, each query's words, each judgment's product and grade —
  so rewording a `why`, a query's kind or split, the order of queries or the
  file's layout move nothing.
- The report says it first: *Golden set `d275da236994`, the one the thresholds
  were set against*, or that the thresholds were set against another one and the
  scores are not comparable with them.
- The gate fails on a mismatch, however good the scores: it refuses to compare
  rather than guess. Measured: one grade lowered in `es.json` left Spanish at
  0.710, over its threshold, and the run still exited 1.
- A unit test (`GoldenSetFingerprintTests`) holds the committed thresholds to the
  committed golden set on every pull request, with no Elasticsearch, and its
  failure gives the new fingerprint.

**Changing the golden set** is therefore one diff with three parts: the judgments,
the thresholds re-derived against them if the scores moved, and the new
`"goldenSet"` beside them, with the reason in the PR. The cleanest comparison for
the reviewer is the base commit and the head both scored against the new
judgments: the difference between them is the code alone.

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

## The claims layer's gates (P10-9, 2026-09-27)

Two more suites share the tool, and since P10-9 they are gates, not only
measurements: `--suite claims` (an extractor against the 71 golden claims in
`knowledge/claims.json`) and `--suite support` (a support scorer against the 40
pairs in `knowledge/support.json`). Their floors are in
**`knowledge/gates.json`**, each beside the measurement it was set from — a
little under what P10-0 measured, so a regression fails and a model's
run-to-run wobble does not:

| gate | floors | measured with |
|---|---|---|
| `claims`, **rules** | P ≥ 0.95, R ≥ 0.28, typing ≥ 0.90, verbatim = 1.0, traps 0, errors 0 | the deterministic `RulesClaimExtractor` |
| `claims`, **chat** | P ≥ 0.88, R ≥ 0.75, typing ≥ 0.83, verbatim ≥ 0.97, language ≥ 0.95, traps ≤ 1, errors 0 | Qwen3.5-4B Q8 at `851bf6e`, llama.cpp v0.5.0, prompt `claims-2026-09-26` |
| `support`, **nli** | AUC ≥ 0.97, accuracy at 0.5 ≥ 0.95, ECE ≤ 0.06 | `bge-m3-zeroshot-v2.0` at `9abf1c8`, TEI 1.9.3 |

**Keyed by the extractor's kind, not the model's name.** The chat floor names
the model, revision and prompt it was measured with; another one is re-measured
before the floor means anything, and the file says so instead of a key quietly
not matching. A scorer or extractor with no floor (the reranker, the yes/no
models) is measured, not judged.

**Where each runs.** CI runs the **rules** gate on every push — deterministic,
no model, seconds. The chat and NLI gates need a 4B model and a 2.8 GB scorer a
runner cannot hold, so they run **at phase close**, against throwaway containers
started with the AppHost's own arguments and limits (never the running stack's):

```bash
MSYS_NO_PATHCONV=1 docker run -d --name gate-support -p 38084:80 -v tendero-tei-models:/data   --memory=3g --cpus=8 ghcr.io/huggingface/text-embeddings-inference:cpu-1.9.3   --model-id MoritzLaurer/bge-m3-zeroshot-v2.0 --revision 9abf1c8aaeb82a2447809c20753ed0b106b76652   --max-batch-tokens 1024 --max-batch-requests 1 --auto-truncate
dotnet run --project tools/SearchEval -- --suite support --scorer nli --tei http://localhost:38084 --ci

MSYS_NO_PATHCONV=1 docker run -d --name gate-extractor -p 38090:8080 -v tendero-extractor-models:/models:ro   --memory=6g --cpus=12 ghcr.io/ggml-org/llama.cpp:server-v0.5.0 -m /models/qwen3.5-4b-q8_0.gguf   --host 0.0.0.0 --port 8080 -c 4096 -np 1 -t 12 --jinja --load-mode none   --chat-template-kwargs '{"enable_thinking":false}'
dotnet run --project tools/SearchEval -- --suite claims --extractor chat --chat http://localhost:38090 --model qwen3.5-4b-q8 --ci
```

`MSYS_NO_PATHCONV=1` is not decoration on Windows: Git Bash rewrote
`/models/…` into `C:/Program Files/Git/models/…`, the server exited before it
listened, and the first gate run scored twenty unreadable answers. **That is the
gate working** — a server that is not there fails the run with every error
counted, rather than passing on no claims at all — but the wait loop that
printed *ready* after timing out was not, and it now fails when the server never
answers.

## What the shop says when it has nothing (2026-09-17)

**The queries with nothing to find are no longer a threshold question.** H1, H4 and
H5 below each tried a floor on a retrieval score and each was refuted. Research
into how shops really do it pointed somewhere else: say plainly that nothing
matches, and decide "we don't sell that" in a query-understanding step beside
retrieval. ADR 0032 records the decision; this section records the measurements.

**The knobs are gone.** `MinimumSimilarity` (H1), the vector weight in the fusion
(H2), `MeaningFirstWithoutFullMatch` (H3), `MinimumRerankScore` (H4) and
`NoAnswerBelowRerankScore` (H5) were deleted with their tests. Each was refuted
where it is recorded: H1–H3 in *The filter the client swallowed*, H4 and H5 in the
paragraphs after its three-hypothesis table. No ranking changed, so no NDCG threshold moved.

**Layer A — the page says what kind of answer it is, with no model.** Every search
with text gets `Exact` when at least one active product carries every word of the
query in `name`, `brand`, `attributesText` or `categoryPathText` — no description,
no fuzziness — and `Related` when none does. The count is one `best_fields` clause
per word, because `brand` has its own analyser and a single `cross_fields` clause
split the fields into groups: against Elasticsearch 9.5.0, "Kuisina cafetera"
counted 0 with the old request body and 1 with the new one. A typo query is
`Related` by design. A query naming a help topic that has a destination
(`returns`, today the only one) also gets a help card.

**Layer B — scope by anchors, refuted on `scope-dev`.** The classifier compares a
query's best cosine to the 23 catalogue categories with its best cosine to 30
categories the shop does not sell, and calls it out of scope when the outside side
wins by a margin. The protocol was declared in the notebook before anything ran:
40 new queries per culture (`tools/SearchEval/scope/`, reasons in
`tools/SearchEval/proposals/2026-09-17-scope-queries.md`), a margin chosen on
`scope-dev` from 0.02, 0.05, 0.08 and 0.12 — the most `out` queries recognised
among margins that mark **no** `in` query out of scope — and one acceptance run.
`bge-m3@5617a9f` on the GPU, both cultures summed per margin:

| margin | `out` recognised | `in` marked out of scope |
|---:|---:|---:|
| 0.02 | 15 / 20 | 9 |
| 0.05 | 14 / 20 | 5 |
| 0.08 | 12 / 20 | 1 |
| 0.12 | 8 / 20 | 1 |

**No margin qualified, so none was chosen**, and there was no `scope-test` or golden
acceptance run. The query no margin saved was "gift for someone who works from
home", told the shop does not sell stationery — in a shop that sells desk lamps and
laptop sleeves. Its Spanish pair, "regalo para alguien que teletrabaja", fell the
same way up to 0.05.

- **A first attempt gave no verdict.** Text Embeddings Inference accepts 32 texts per
  request, and one culture's anchors are 133; the build was refused, so layer B could
  never have started in production either. Batches of 32 fixed it, test-first, and
  the rerun gave the verdict. Anchors, queries, grid and rule were not touched.
- **Latency**, which closes a deviation from the design (layer B embeds the query
  itself, because the vector port returns ids, not vectors): a query embedding
  costs p50 11.1 ms and p95 13.6 ms; the anchors take 324–446 ms per culture cold.
- **Layer B is evaluation tooling now**, decided the same day (ADR 0032, amended).
  Keeping it off in `src/Search` bought nothing — with no calibration file it could
  never register — and cost unreachable code, an `OutOfScope` on the wire nothing
  could send, and eleven mutation survivors no test could kill. The classifier, the
  anchor file, the protocol's rules and the 40 scope query pairs per culture are
  `tools/SearchEval/QueryScope`, and `--suite scope` still runs the protocol whole:
  everything above is reproducible, and it is the exam a trained classifier has to
  sit. The shipped answer has two values.

**What the gate says now.** The third gate run is recorded below, in *The third gate
run, and hybrid becomes the default*. On what the page says: every query with
nothing to find is admitted (1.00, both cultures, every strategy) and the notice
rate is 0.481 (en) and 0.462 (es). No scored query was told it is out of scope —
trivially, since layer B was already off, and since it moved out of the shipped
code no page can say it at all.

### Where the notice lands, read by query kind (2026-09-17)

The overall rate says how often the notice fires and nothing about whether it was
deserved, so the report breaks it down by Baymard query kind. Lexical only, since
the notice is decided beside retrieval and no strategy has a say in it; both
cultures cleared their thresholds on the run
(`docs/blog/reports/2026-09-17-no-answer-research/notice-by-kind.md`, read in
`notice-by-kind-reading.md`).

| kind | queries (en \| es) | notice rate en | notice rate es |
|---|---|---:|---:|
| exact | 3 \| 3 | 0.000 → 0.000 | 0.000 → 0.000 |
| compatibility | 3 \| 3 | 0.000 → 0.000 | 0.000 → 0.000 |
| product-type | 13 \| 12 | 0.385 → 0.385 | 0.250 → 0.250 |
| feature | 15 \| 16 | 0.400 → **0.333** | 0.375 → **0.313** |
| abbreviation | 3 \| 3 | 0.667 → **0.000** | 1.000 → **0.333** |
| symptom | 3 \| 3 | 1.000 → 1.000 | 1.000 → 1.000 |
| use-case | 12 \| 12 | 1.000 → 1.000 | 1.000 → 1.000 |

**Every use-case and symptom query gets the notice, and there it is the most honest
sentence on the page**: "gift" and "regalo" appear in none of the hundred products,
so a page of kitchen linen is a guess a shopper deserves to be told about. 30 of the
55 notices were that case. The other 25 sat on pages a shopper would call correct —
13 of them answered at 0.90 or better — and reading them found three mechanical
causes: a bare measurement, a typo, and a word that lives only in the description.

**Only the first was fixed.** A query word that is a number with at most a short
unit glued to it is dropped from the full-match check (ADR 0032; the search still
ranks with the whole query). Measured on the same corpus, after
(`notice-by-kind-after.md`): **0.538 → 0.481 (en), 0.519 → 0.462 (es)**, NDCG@10 and
recall@50 identical in both cultures — 0.748 / 0.795 and 0.692 / 0.738 — because no
ranking changed. Six notices went, all on pages answered at 0.634 or better:
`24cm frying pan`, `6l pressure cooker`, `laptop backpack 15 inch`, `sarten 24cm`,
`olla 6l`, `mochila para portatil 15 pulgadas`. Nothing honest was silenced.

Three narrower signals were measured on the same run and not taken: counting the
description too (0.481 / 0.481, but it silences `slip-on shoes` at 0.335 and
contradicts ADR 0032), fuzziness per word (0.500 / 0.462, and it wrongly silences
`bambas para correr` at 0.000), and reading only `name` and `categoryPathText`,
which goes the wrong way and *adds* nine notices on queries scoring 1.000.

**The notice rate is an open decision for the author.** About half of the queries
that have real answers are told nothing matches exactly. It is true of the words —
those queries name no product word for word — and it may still be too often for a
shop to say.

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

### The third gate run, and hybrid becomes the default (2026-09-17)

**`SearchVector`'s removal condition is met, and the flag is deleted.** The
condition read: hybrid clears its own committed thresholds and beats lexical on
NDCG@10 in both cultures in three consecutive gate runs. Runs one and two are the
identical 2026-09-15 runs above. Run three went once, on the GPU, against
throwaway engines — Elasticsearch 9.5.0, Qdrant v1.18.0, and TEI `sha-4150561`
serving `bge-m3@5617a9f` and `bge-reranker-v2-m3@953dc6f` — and hybrid was
reranked, because reranking is a capability now and a GPU gate has a reranker:

| NDCG@10 / recall@50 | lexical | hybrid (reranked) | thresholds, hybrid |
|---|---:|---:|---:|
| en | 0.748 / 0.795 | **0.843 / 0.934** | 0.79 / 0.92 |
| es | 0.692 / 0.738 | **0.831 / 0.928** | 0.77 / 0.91 |

Every figure equals its 2026-09-15 counterpart. Hybrid clears its bars and beats
lexical with or without the reranker (0.810 / 0.783 without it), so the condition
holds either way. No scored query was told it is out of scope (false out-of-scope
0 for every strategy and culture — trivially, since layer B was off, and the column
left the report with the classifier the same day); the notice rate was 0.538 (en) and 0.519 (es) — measured before the
measurement-token rule, which took it to 0.481 / 0.462 — the same for all strategies; and every query with nothing to find was admitted
(1.00), while hybrid still shows ten products on those pages. Hybrid answers every
search now; lexical is what it degrades to, and CI still gates lexical on its own
thresholds. The report is private, under
`docs/blog/reports/2026-09-17-no-answer-research/gate-run-3.md`.

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
above lexical's in both cultures for the first time. The third gate run, on
2026-09-17, reproduced every lexical figure exactly, so nothing here moved. The 942 / 985-judgment
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

## The catalogue grew to 120 products, and the set with it (2026-10-08)

Twenty products were added in families the shop did not have — furniture,
plants, decoration, the bathroom (`docs/specs/catalogue-growth.md`). **Nothing
was replaced and nothing judged before was judged again**, which is what lets
the numbers before and after sit in one table:

| NDCG@10 | 100 products, 52 scored queries | The same 52, 120 products | 63 scored queries, 120 products | Floor now |
|---|---:|---:|---:|---:|
| lexical es | 0.692 | 0.685 | 0.714 | 0.69 |
| lexical en | 0.748 | 0.744 | 0.773 | 0.75 |
| hybrid + reranker es | 0.831 | 0.822 | 0.839 | 0.82 |
| hybrid + reranker en | 0.843 | 0.834 | 0.855 | 0.83 |

recall@50 over the 63: lexical 0.746 es / 0.806 en (floors 0.73 / 0.79), hybrid
0.924 / 0.943 (floors 0.91 / 0.92, unmoved). Throwaway engines, the versions of
the first measurement; 1256 (es) and 1273 (en) judgments, every strategy's first
ten judged.

- **The second column is what twenty more products cost** the queries that
  were already there: four to nine thousandths. A coffee table for *coffee
  maker*, a standing desk for *electric kettle*, a bed and two plants that want
  light for *luz para leer en la cama*.
- **The third is not search getting better.** Eleven queries joined — ten new
  pairs and *house plant*, which had nothing to find until the shop sold a
  monstera — and they are easy: one obvious product each. The floors were
  re-derived from the whole set because the old ones, under a mean lifted by
  easy queries, would pass a real regression. The test that says the lexical
  bar does not move was changed on purpose, and says why.
- **Seen once and not explained**: one gate run of six gave lexical es 0.706
  with two results unjudged, while another program was starting a dozen
  containers on the same machine. Five runs agree to the third decimal. If it
  is the gate searching before its index has refreshed, a loaded CI runner
  will find it again; recorded so that it is recognised.

## Words against the pictures, alone (2026-10-08)

The images spec keeps text against pictures out of `/api/search` *until its
effect on NDCG@10 is known*. This is the first half of that: what the pictures
rank **on their own**, over the same golden set, as a diagnostic strategy of
the tool (`--strategy hybrid --siglip models/siglip2`) that gates nothing.
SigLIP 2 at revision `75de2d5`, our export, in process on the CPU; a throwaway
Qdrant v1.18.0; the query embedded as text, the nearest catalogue pictures,
each product once.

| NDCG@10, 63 scored queries | lexical | hybrid, fused only | words' vectors alone | **pictures alone** |
|---|---:|---:|---:|---:|
| en | 0.770 | 0.830 | 0.833 | **0.742** |
| es | 0.715 | 0.800 | 0.788 | **0.713** |
| recall@50 en / es | 0.806 / 0.746 | 0.943 / 0.924 | 0.982 / 0.967 | 0.821 / 0.811 |

Read with three cautions, each of which makes the pictures' number a **lower
bound**:

- **Only 96 of the 120 products have a picture.** Twenty-four cannot be found
  this way at all, whatever is asked.
- **A quarter to a third of its first ten are unjudged** (`judged@10` 0.760 en,
  0.679 es, where the other strategies are at 0.99): the golden set was judged
  from what the words' strategies returned, and an unjudged result scores as
  irrelevant. The pictures bring back products nobody graded.
- The hybrid column is **without the reranker** — fused by rank only — so it is
  not the 0.839 / 0.855 of the table above and its *FAIL* against floors set
  with the reranker means nothing here. The run was not a gate.

By the kind of query, where it stops being one number:

| kind | queries en / es | lexical en / es | hybrid en / es | pictures en / es |
|---|---|---|---|---|
| product-type | 21 / 20 | 0.815 / 0.808 | 0.881 / 0.863 | **0.900 / 0.889** |
| feature | 17 / 18 | 0.897 / 0.855 | 0.897 / 0.845 | 0.732 / 0.706 |
| use-case | 13 / 13 | 0.466 / 0.303 | 0.629 / 0.603 | 0.618 / 0.533 |
| symptom | 3 / 3 | 0.545 / 0.347 | 0.519 / 0.435 | 0.428 / **0.513** |
| exact | 3 / 3 | 1.000 / 1.000 | 1.000 / 1.000 | 0.639 / 0.639 |
| compatibility | 3 / 3 | 0.990 / 0.985 | 0.991 / 0.987 | 0.652 / 0.626 |
| abbreviation | 3 / 3 | 0.828 / 0.850 | 0.934 / 0.935 | 0.734 / 0.712 |

- **A picture knows what a thing is, and nothing written on its box.** Asked
  for a *kind* of product — *kettle*, *sofa*, *zapatillas* — the pictures alone
  rank better than the fused search, in both languages, with a quarter of their
  answers ungraded. Asked for a product by its exact name or for what fits what,
  they are the worst thing in the table: a model number is not visible.
- **Spanish costs the pictures almost nothing** (0.713 against 0.742), where it
  costs the lexical index five points. One model reads both languages and a
  picture has neither.
- So the second half of the question — whether it helps **as a leg** — is worth
  asking, and it is not answered here: it needs the pictures fused with the
  other two and the newly returned products judged first, or the fusion would
  be scored against a ruler that cannot see a third of what it adds.

**What a floor would separate.** A nearest neighbour always exists, so each
question needs a similarity below which it answers nothing. Cosine of the
nearest catalogue picture:

| Asked with | n | Least | Tenth | Median | Ninth tenth | Most |
|---|---:|---:|---:|---:|---:|---:|
| a catalogue picture — its nearest *other* product | 96 | 0.724 | 0.806 | 0.863 | 0.927 | 0.971 |
| a drawn shape that is no product (blank, noise, gradient, a page of text) | 4 | 0.638 | 0.638 | 0.688 | 0.743 | 0.743 |
| words (en) — the first picture is a right answer | 43 | 0.075 | 0.093 | 0.136 | 0.155 | 0.167 |
| words (en) — the first picture is not | 20 | 0.077 | 0.080 | 0.113 | 0.135 | 0.142 |
| words (en) — for something the shop does not sell | 7 | 0.069 | 0.069 | 0.074 | 0.077 | 0.092 |
| words (es) — the first picture is a right answer | 45 | 0.073 | 0.095 | 0.133 | 0.152 | 0.162 |
| words (es) — the first picture is not | 18 | 0.074 | 0.081 | 0.102 | 0.133 | 0.138 |
| words (es) — for something the shop does not sell | 7 | 0.061 | 0.063 | 0.066 | 0.082 | 0.086 |

- **For a picture there is no floor to be had from this.** A page of text is
  as near a catalogue picture (0.743) as the least similar pair of real
  products (0.724): the catalogue's pictures share a painter, a background and
  a light, and that alone is most of the similarity. And the seed holds no
  photograph of something the shop does not sell, so the lower distribution is
  four shapes — a lower bound for *nothing*, not what a shopper photographs.
  `MinPictureSimilarity` stays unset, and the storefront has to say *the
  nearest things we have* rather than *we found these*.
- **For words there is the beginning of one.** What the shop does not sell tops
  out at 0.092 (en) and 0.086 (es); nine in ten right answers are above 0.093.
  A floor near 0.08 would have silenced five or six of the seven and cost a few
  right answers in a hundred — on seven queries a culture, which is a
  direction and not a number. `MinTextSimilarity` stays unset too; it is the
  author's to set with these beside it.

**What it costs**, on the CPU of the machine it was measured on: 64 ms to embed
a text and 267 ms a picture, decoding and resizing included, the first call's
model load amortised over 96 — after the runtime was told how many threads to
use. Left to its default it was 334 ms and 782 ms (see the images spec, *I-6*).

## The pictures' pool, judged — and the pictures as a third leg (2026-10-08)

The second half of the question above. In the order it had to go: judge what
the pictures bring back, then fuse, then compare.

**Judged.** 360 results that the pictures put in a first ten and nobody had
graded: 153 in English, 207 in Spanish (1,426 and 1,463 judgments now).
Each took, in order, the grade its pair already had in the other language; a
grade decided for it with a reason; for a brand query, 3 or 0 by its brand; and
otherwise 0.

| Grade | Results | What they are |
|---|---:|---|
| 0 | 306 | a product that only LOOKS like the answer: a round mirror for a lamp, a bamboo rack for a chair, a toaster for a coffee maker |
| 1 | 44 | mostly gifts — a kitchen appliance for a grandmother, possible and impersonal |
| 2 | 7 | a toaster and a blender as *a gift for the kitchen*, an olive tree for a grandmother |
| 3 | 3 | the bike pannier for *azul marino* — navy is one of its three colours, which only its variants say — and two products of a brand asked by name |

**Eighty-five in a hundred of what the pictures add at the tail is noise**, and
that is the number to keep beside the one in the section above: the pictures
are right about what a thing is, and their fifth to tenth results are whatever
is the same shape.

**The pictures alone, with nothing unjudged**: 0.751 en / 0.726 es (it was a
lower bound of 0.742 / 0.713), recall@50 0.827 / 0.820.

**The ruler moved, and the recall floors with it.** Fifty-four products are now
known to be relevant that the words' strategies were never asked for, so the
same engine finds a smaller share of what there is to find. Three runs alike:

| | NDCG@10 before → now | recall@50 before → now | recall floor before → now |
|---|---|---|---|
| lexical es | 0.711 → 0.709 | 0.746 → 0.733 | 0.73 → **0.71** |
| lexical en | 0.768 → 0.767 | 0.806 → 0.798 | 0.79 → **0.78** |
| hybrid + reranker es | 0.839 → 0.836 | 0.924 → 0.905 | 0.91 → **0.89** |
| hybrid + reranker en | 0.855 → 0.848 | 0.943 → 0.932 | 0.92 → **0.91** |

The NDCG floors did not need to move and did not. `--ci` passes on the engine
as it ships.

**The third leg.** `Search:Hybrid:PictureDepth` — zero, which is off — makes
the pictures a third list in the fusion: the query embedded as text by the
picture model, the nearest catalogue pictures, each product once, fused by rank
with the words and their meaning, and the head reranked as always. Hybrid with
the reranker, three runs of each, identical to the third decimal:

| | NDCG@10 en | NDCG@10 es | recall@50 en | recall@50 es | mean ms en / es |
|---|---:|---:|---:|---:|---:|
| two legs (as it ships) | 0.848 | 0.836 | 0.932 | 0.905 | 98 / 96 |
| + pictures, depth 10 | **0.853** | **0.852** | **0.956** | **0.965** | 247 / 197 |
| + pictures, depth 20 | 0.855 | 0.849 | 0.964 | 0.969 | 249 / 203 |

| kind (depth 10 against two legs) | en | es |
|---|---|---|
| product-type | 0.865 → 0.873 | 0.895 → 0.905 |
| use-case | 0.658 → 0.670 | 0.608 → 0.657 |
| abbreviation | 0.985 → 0.987 | 0.933 → 0.968 |
| feature | 0.920 → 0.920 | 0.896 → 0.900 |
| symptom | 0.704 → 0.704 | 0.646 → 0.652 |
| exact, compatibility | unchanged | unchanged |

- **It helps, and nothing gets worse.** Half a point in English, a point and a
  half in Spanish, and no kind of query below where it was — including the
  exact names the pictures alone were worst at, because the reranker reads the
  text of whatever the pictures propose and the junk does not survive it. The
  pictures propose; the words decide.
- **What it really buys is recall**: six points in Spanish. Products the words
  and their meaning never fetched are in the first fifty.
- **Spanish gains three times what English does**, as the pictures alone
  suggested it would: a picture has no language.
- **It costs a hundred to a hundred and fifty milliseconds a search**, the text
  tower on the CPU — more than the rest of the search together. Ten is the
  depth: twenty buys nothing in NDCG and starts returning unjudged products.
- **Off in the code, on where the pictures are** (the author, 2026-10-09). The
  default stays zero — what a shop without the picture model runs, and what
  the floors above are held against — and the AppHost sets ten for the API
  wherever visual search is already running, so the model it needs is the one
  already loaded. It is a setting; nothing else changes, a shop without the
  picture model searches on two legs, and a picture leg that fails or is slow
  costs only itself — counted as its own stage, `pictures`.
- Eleven hand mutations of the leg, ten red. The survivor is equivalent: asking
  a shop that has no model is answered by the stand-in saying so, which the
  leg treats as the leg not being there.

## Narrowing a search by the category its words name: refuted (2026-10-21)

Tino's search for *cafeteras* found pans too, and its *See the 11 results*
counted them (the board, 2026-10-14). The first fix was to narrow the search to a
category whenever its words named one in the shop's own taxonomy. The narrowing
was exact, with no embeddings:

- each category name was split into the things it names (*Flexos y lámparas de
  escritorio* → flexos, desk lamps);
- singular and plural matched, with accents folded;
- several names on one branch narrowed to the deepest, and names on two branches
  narrowed to none.

It was held to one line: **no query of this golden set may hide a product it
grades 2 or 3.**

| | es + en |
|---|---:|
| Queries | 140 |
| Narrowed to a category | 57 |
| Narrowed and hid relevant products | **12** |
| Relevant products hidden | **45** |

Where the taxonomy and the shopper part ways:

- *mochila* / *backpack* → Backpacks hides the travel backpack, filed under Luggage;
- *ropa deportiva* / *sportswear* hides the running shoes and tights;
- *ordenar la cocina* / *organising the kitchen* → Kitchen hides all of Storage;
- *setting the table* → Tables hides the tableware;
- *bedside table* hides the nightstand, filed under Bedroom;
- *ropa de abrigo* → Coats hides gloves and hats.

**Not built.** A category is where a product is filed, and a shopper's words say
what it is for. Tino now uses the category another way, which hides nothing. The
cards code adds come from the category of the products the model chose, among the
search's own results. *See all* becomes a filter that the listing shows and the
shopper can take off. The matcher and its test were deleted; this table is what is
left of them (`docs/plans/2026-10-21-tino-product-list.md`).
