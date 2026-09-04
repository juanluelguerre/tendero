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

Measured against the seed sample, lexical BM25 only. Five consecutive runs give
identical numbers.

| culture | NDCG@10 | recall@50 | threshold NDCG | threshold recall |
|---|---:|---:|---:|---:|
| es | 0.819 | 0.642 | 0.81 | 0.63 |
| en | 0.766 | 0.556 | 0.75 | 0.54 |

Measured against 100 products and 268 judgments; identical across three
consecutive runs. See the section above for why these replaced 0.943 / 0.937.

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
3. **Vocabulary gaps lexical search cannot bridge** — down to one query per
   culture: `gift for the kitchen` and `regalo para casa`. Nothing in the
   catalogue contains the word "gift", so no taxonomy reaches them. This is the
   semantic-intent gap, and it is the before/after number hybrid search has to
   earn.

A cautionary note on annotating: "flexo con usb" was originally annotated as a
synonym gap lexical search could not solve. It scores 1.000, and always did —
the Spanish description literally begins "Flexo LED". The annotation was written
from assumption instead of from the catalogue. That is precisely what the `why`
field exists to make visible in review.
