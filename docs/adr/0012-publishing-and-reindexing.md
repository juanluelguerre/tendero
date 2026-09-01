# 0012 — Publishing is an explicit step, and the index is rebuildable

**Status**: accepted

## Context
The catalogue was published and unfindable, and both halves of that were
designed in.

`Product.Create` sets `Status = Draft`, because imported means awaiting review.
The search query filters `status: active`. And **nothing in `src/` ever called
`Publish()`** — zero call sites in production code. So an imported catalogue was
invisible by construction, on any machine, forever. The quality gate did not
catch it because `tools/SearchEval` calls `product.Publish()` itself, with a
comment saying exactly why: *"aquí no hay cola de revisión que lo publique"*. The
gap was written down, in the right place, by the person who worked around it.

Separately, Elasticsearch is declared `ContainerLifetime.Persistent` **without a
volume**, while Postgres has one. When Aspire recreated the container, Postgres
kept six Active products and the index came back empty — and nothing could
repair it. `SearchIndexInitializer` is idempotent and found the indexes already
there; the outbox had delivered all 52 of its messages; and republishing an
already-active product reports `alreadyActive` and saves nothing, by design.
`docs/architecture.md` had always promised the index could be rebuilt from
Postgres at will. The "at will" had no implementation.

## Decision
**Publishing is a domain operation with its own slice**, not a side effect of
importing. `POST /api/catalog/products/{id}/publish`, consumed today by the
backoffice review queue and tomorrow, unchanged, by whatever approves
AI-enriched copy. Importing never publishes: the Draft state is the review
queue's reason to exist, and an import flag that skipped it would delete the
only moment a human sees the product.

**Publishing is idempotent and reports it.** Already-active returns
`alreadyActive` and saves nothing. Calling `Publish()` again would raise another
`ProductUpserted` and put the indexing worker to work rewriting an identical
document — outbox noise for an operation that changed nothing.

**The index stays without a volume, and `POST /api/search/reindex` is the
answer.** Keeping it disposable is the point (ADR 0004: the index is a
projection, Postgres is the truth); what was missing was the way back. The
rebuild **replays `ProjectProductToIndex`'s rule** over every product rather
than inventing a second one — Active is indexed, everything else removed — so it
*converges* instead of merely adding. It does not recreate the indexes or
restate their mappings: that belongs to `SearchIndexInitializer`, and a
duplicated mapping is two sources for one truth.

## Consequences
A fresh clone needs two steps, not one: import, then publish. That is the honest
shape of a catalogue with a review stage, and the backoffice queue makes it a
button rather than a `curl`.

Reindex converges for products that **exist**. A row deleted straight from the
table leaves an orphan document, because the walk cannot see what is no longer
there — observed: six hard-deleted rows left twelve documents for six products.
Through the application it cannot happen (the domain archives, never deletes,
and `Archive()` raises `ProductArchived`, which removes the document); it takes a
manual `DELETE` or a restored snapshot. Closing it means dropping the indexes
before rebuilding, which moves mapping ownership out of the initializer — a
trade we decline until something other than a developer with `psql` produces the
case.

At full-catalogue scale the rebuild belongs in a background job rather than an
HTTP request. It streams (`AsAsyncEnumerable`, no tracking) so memory is not the
limit, but a request that walks 147k products is still the wrong shape. It moves
with the full-dataset import, which is when there are numbers worth publishing.
