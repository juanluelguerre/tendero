# Search evaluation

## Golden set

`tools/SearchEval/golden/{culture}.json` — 50–100 curated queries per culture:

```json
{
  "query": "zapatillas running mujer",
  "judgments": [
    { "productId": "…", "relevance": 3, "why": "exact category + gender match" },
    { "productId": "…", "relevance": 1, "why": "running shoe, wrong gender" },
    { "productId": "…", "relevance": 0, "why": "backpack — brand noise" }
  ]
}
```

Cover: easy head queries, synonyms, typos ("zapatilas"), ambiguous queries,
attribute queries ("cafetera 12 tazas"). The `why` field is what makes
annotations reviewable in PRs.

## Metrics

- **NDCG@10** — graded ranking quality vs the ideal order (gain discounted by
  log of position, normalized to 0–1).
- **recall@50** — did the relevant items appear at all (guards the retrieval
  stage independently of ranking).

## CI gate

`dotnet run --project tools/SearchEval -- --ci` boots Elasticsearch, imports
the seed, indexes, scores both cultures, and fails below the thresholds in
`eval.thresholds.json`. Threshold or annotation changes require justification
in the PR body. The report publishes as a CI artifact.
