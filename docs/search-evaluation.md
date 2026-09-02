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

Two things make the run reproducible, and both were learned the hard way:

- **Drop the indexes first.** Product ids are GUID v7 minted per run, so without
  it the previous run's documents stay and compete in the ranking. Two runs of
  identical code scored 0.674 and then 0.360.
- **Refresh explicitly after indexing.** Elasticsearch refreshes once a second,
  so otherwise the score depends on whether the refresh landed before or after a
  query: 0.860 and then 0.769. Polling until *some* query returns a hit is not
  enough — it only proves one document is visible, not all six.

## Committed baseline

Measured against the seed sample, lexical BM25 only. Five consecutive runs give
identical numbers.

| culture | NDCG@10 | recall@50 | threshold NDCG | threshold recall |
|---|---:|---:|---:|---:|
| es | 0.860 | 0.841 | 0.85 | 0.83 |
| en | 0.811 | 0.773 | 0.80 | 0.76 |

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

## Known gaps the baseline still exposes

The queries scoring 0.000 are diagnosis, not noise:

1. ~~**Attribute values are never translated.**~~ **Closed 2026-09-02** — see
   the before/after above. Colour and gender are option codes now, with a label
   per culture, and the two queries this cost went from 0.000 to 1.000.
2. **The category taxonomy is not user vocabulary.** "induction cookware" fails
   because `COOKWARE` is a code, not a label someone would type. It needs the
   category to become localized text, not a mapping tweak.
3. **Vocabulary gaps lexical search cannot bridge** — "reading light with usb",
   "ropa deportiva", "menaje de cocina", "gift for the kitchen". These are in the
   golden set deliberately: they are the headroom hybrid search has to earn in
   phase 2, and the before/after number for that article.

A cautionary note on annotating: "flexo con usb" was originally annotated as a
synonym gap lexical search could not solve. It scores 1.000, and always did —
the Spanish description literally begins "Flexo LED". The annotation was written
from assumption instead of from the catalogue. That is precisely what the `why`
field exists to make visible in review.
