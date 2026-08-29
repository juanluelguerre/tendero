# 0004 — BM25 as permanent fallback; one index per language

**Status**: accepted

## Context
AI dependencies (Ollama, Qdrant) can be down; the store must keep searching.
Spanish and English need correct stemming and stopwords.

## Decision
Lexical search (Elasticsearch BM25) is a hard dependency and the permanent
degraded mode; hybrid/AI layers are feature-flagged on top. One index per
culture (`products_es`/`products_en`) with native analyzers; documents carry
text pre-resolved via `LocalizedText`. Indexes are disposable projections
rebuilt from Postgres.

## Consequences
The public `/api/search` contract never changes as layers evolve. Relevance is
guarded by the golden-set NDCG gate, not by opinion.
