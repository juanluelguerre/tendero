# 0058 — Pictures are a vector space of their own: one point a picture, asked beside the search and never inside it

**Status**: accepted (phase 13a, 2026-10-08). The rulings are the images spec's,
taken by the author when it was accepted — the model in process, a collection
keyed on the picture, endpoints of their own, an upload that is never kept.
What implementing them added is marked *found while building*.

## Context

The shop can read a picture (ADR 0031, the local model's projector) and its
catalogue can say what a picture shows (ADR 0057). What it could not do is be
*searched* with one. Three things had to be decided before a line of it was
written, because each is expensive to move later:

- **Where the model runs.** SigLIP 2 embeds a picture and a text into the same
  space. It could be a sidecar in Python or a model in the .NET process; the
  roadmap left that fork open behind one gate — whether the text tower's
  tokenizer could be reproduced exactly.
- **What a vector belongs to.** A product has several pictures, a picture may
  be shared by several products, and a product raises `ProductUpserted` for
  every price and stock change.
- **What it is to the search.** Hybrid search is the shop's measured ranking
  (ADR 0023): a golden set, floors, a gate in CI. A third leg changes that
  number in ways nobody has measured.

## Decision

**In process.** SigLIP 2 (`google/siglip2-base-patch16-224`, Apache-2.0, pinned
to a revision — ADR 0031) is exported by us to ONNX and run by ONNX Runtime
inside the API and the worker, behind `IImageEmbeddingGenerator`. The gate was
passed first: our tokenizer gives the maker's ids for every fixture text, and
our export the maker's embeddings (0.99999994 for the export, 0.999998 for a
picture through our own preprocessing). No sidecar, no Python at run time.

**One point a picture, in a collection of its own.** `IPictureVectorIndexer`
and `IPictureSearch`, with Qdrant behind them:

- The point is keyed on the picture's id, which is the hash of its bytes. The
  same file under two products is one point naming both, embedded once; a
  product upserted for a price finds its pictures already there. Nothing
  compares anything: the dedup is a property of the key.
- The collection is named for the model, its revision and its size, and is
  never the words' collection. Every point says which model wrote it, and one
  that disagrees is refused when read.
- It is a projection like the others: fed by the outbox under
  `ProductIndexProjection`, never failing a message, rebuilt when empty,
  rebuilt by hand with `reindex?target=pictures`.

**Asked beside the search, never inside it.** Two endpoints of their own:
`POST /api/search/by-image` and `GET /api/search/pictures`. `/api/search` does
not know they exist. Text against pictures becomes a leg of the hybrid search
only when its effect on NDCG@10 is a number — and the first half of that
number says it is worth asking (alone it ranks about as the lexical index, and
above the fused search for a *kind* of product; `docs/search-evaluation.md`).

**A shopper's picture is never kept.** Read in memory, decoded once to the
model's 224 by 224 pixels, embedded, dropped. Not the image store, not a log,
not a trace; a span carries its width, height and format. Both endpoints are
`AllowAnonymous` by explicit decision and share one rate limit of their own.

**Without it, nothing changes** (invariant 8). There is no switch: the folder
of the export is the opt-in. A shop without the folder or without the vector
store gets named stand-ins — writing is a no-op, asking is a 503 that says
visual search is not here — and a status the storefront reads first.

## Found while building

- **The upload is not re-encoded**, though the spec said it would be, to drop
  its metadata. Nothing re-encoded is ever needed, and a copy that exists only
  so its EXIF can be removed is a copy that need not exist.
- **Which way up is read; nothing else of the metadata is.** A phone stores a
  portrait as a landscape and a note. Unturned, a photograph is embedded lying
  on its side.
- **`reindex?target=all` does not include the pictures.** *All* stays the two
  projections of the words, so a shop without visual search can still rebuild
  everything it has.
- **The runtime is told its threads** — half the logical processors, at most
  eight. Its default was the slowest setting measured (695 ms a picture
  against 121 ms).
- **The API loads the model as it starts**, in the background: the first
  search otherwise waited 18.7 s for it.

## Left open, on purpose

- **The two similarity floors are unset.** A nearest neighbour always exists.
  For a picture the seed cannot show a floor at all — a page of text is as near
  a catalogue picture as two unlike products are, because the catalogue's
  pictures share a painter — and for words it shows only a direction, on seven
  queries a culture. They are settings (`Search:Images`), and the author's to
  set.
- **Whether text against pictures helps as a leg.** It needs the products it
  newly returns judged first: a third of its first ten are outside the golden
  set, and a fusion scored today would be punished for every new right answer.
- **Concurrency.** A picture decoded is tens of megabytes for a moment, and the
  rate limit bounds a client, not the sum of clients. Laboratory scale; the
  number that would force a queue is not known and is not guessed here.

## Consequences

- The API holds about 1.5 GB of model in memory once warm, and the worker the
  vision tower's 372 MB while it embeds. A deployment without visual search
  holds none of it.
- A second model revision is a second collection by construction; moving to it
  is a reindex, never a migration, and never a mixed space.
- The model's weights stay outside git (`models/siglip2`, prepared by
  `src/AppHost/scripts/prepare-siglip.py`), so the tests that hold the export to
  its maker run where the model is and not in CI. What runs everywhere is
  everything around it: the preprocessing, the collection against a real
  Qdrant with a fake model, the endpoints and their degraded paths.
- Catalog is untouched. A picture's vector is Search's projection of a
  picture, as a search document is its projection of a product.
