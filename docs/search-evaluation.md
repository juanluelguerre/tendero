# Search evaluation

## Golden set

`tools/SearchEval/golden/{culture}.json` — 50–100 curated queries per culture
(22 today, against the 6-product seed sample):

```json
{
  "culture": "es",
  "source": "seed",
  "queries": [
    {
      "query": "zapatillas running mujer",
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

Dropping the indexes first is not optional: product ids are GUID v7 minted per
run, so without it the previous run's documents stay and compete in the ranking.
Two runs of identical code scored 0.674 and 0.360 before that was fixed.

## Committed baseline

Measured against the seed sample, lexical BM25 only:

| culture | NDCG@10 | recall@50 | threshold NDCG | threshold recall |
|---|---:|---:|---:|---:|
| es | 0.769 | 0.750 | 0.76 | 0.74 |
| en | 0.674 | 0.636 | 0.66 | 0.63 |

## Known gaps the baseline exposes

The queries scoring 0.000 are diagnosis, not noise. Three distinct causes, all
phase-2 work, all now measurable:

1. **`best_fields` + `AND` does not span fields.** "zapatillas running mujer"
   returns nothing: the first two terms live in `name`, `mujer` lives in
   `attributesText`, and no single field holds all three. Same for "camiseta
   hombre" and "laptop backpack 15 inch". `cross_fields` is the likely fix, and
   this gate is what will prove it.
2. **Attribute values are never translated.** Colour and gender are stored in
   Spanish, so "navy blue shoes" and "womens running shoes" cannot match in the
   English index. Localized attribute values are deferred on purpose
   (initial-plan §7); this is what their absence costs.
3. **Vocabulary gaps lexical search cannot bridge** — "flexo con usb",
   "reading light with usb", "ropa deportiva", "menaje de cocina". These are in
   the golden set deliberately: they are the headroom hybrid search has to earn
   in phase 2, and the before/after number for that article.

Note `category` is mapped as a `keyword` yet listed among the `multi_match` text
fields, so "induction cookware" never matches category `COOKWARE`. That one is
arguably a mapping bug rather than a missing feature.
