# ADR 0018 — The AI writes claims, not products

Status: accepted · 2026-09-27 · Related: [ADR 0014](0014-context-map.md), [ADR 0031](0031-model-weights-are-dependencies.md), [ADR 0012](0012-publishing-and-reindexing.md) · Analysis: [`claim-extraction-models.md`](../analysis/claim-extraction-models.md) · Roadmap: B4

## Context

A product's description already says most of what a shopper compares — *"apta
para inducción"*, *"se seca en una hora"*, *"cierre que no abre bajo presión"* —
and says it in prose nothing can filter, compare or cite. In the seed catalogue,
**54 of the 71 facts** a person annotated in twenty descriptions are carried by no
imported attribute (`P10-0`). A generative model can read them out; a model can
also invent them, and a catalogue an agent buys from cannot tell the difference
unless the catalogue keeps it.

Two shapes were rejected in the roadmap (B4, question 5). **An extension of
`Catalog`** would give `Product` two writers — a shopkeeper editing and a model
proposing — with two lifecycles and two standards of truth. **A projection** is
rebuildable from other truth, and a person's approval of a claim is truth that
exists nowhere else.

## Decision

**1 · `Knowledge` is a bounded context, and it never writes to `Catalog`.** It
owns what the shop can *say* about a product: the documents a statement rests on,
the claims, and their review. Products are referred to by `ProductId`, a
SharedKernel value (ADR 0014). An approved claim raises `ClaimApproved`; through
the outbox, a `Catalog` handler applies it (`SetAttribute`) and a `Search` handler
adds it to what is indexed. **Propose, approve, apply** — three steps, three
auditable writers, none of them a model.

> **Amended 2026-09-27, when it was built (P10-6).** Search gets no handler and
> no `specsText` field. A claim is read *from* the description, and the
> description is already indexed: a statement adds a paraphrase of words the
> index holds, and a searchable field made of paraphrases would move the NDCG
> gate for no new word. What a claim adds that the index lacks is a **typed
> value**, and that reaches Search the way every attribute does — Catalog's
> `SetProductAttribute` raises `ProductUpserted`, and the ordinary projection
> renders it into `attributesText` and the facets. Search keeps no reference to
> Knowledge, and an untyped statement stays in Knowledge for the comparison and
> the product page. Catalog has the last word on the value: its definition
> accepts it or the command is refused, audited and counted
> (`tendero.knowledge.claims.applied`). A superseded claim leaves the attribute
> where it is — the value was a person's decision and is Catalog's now; a new
> text's claims, approved, overwrite it.
>
> **And Catalog's answer goes back onto the claim (P10-7).** The worker records
> how Catalog holds the applied value (`AppliedAs`, its culture-free key) or why it
> refused, through Knowledge's own command. A spec row shows a claim's words only
> where the product holds exactly the value the claim put there — so a reimport
> that changed it never leaves a quote beside a value it does not support.

**2 · The model.**

```
SourceDocument(SourceDocumentId = SHA-256 of the text, SourceKind, Origin?, Text, RetrievedAt)

ProductClaim(ClaimId, ProductId, LocalizedText Statement, AttributeCode?, Value?,
             Evidence[], Provenance, Status, ReviewedBy?, ReviewedAt?)

Evidence(SourceDocumentId, Quote, SpanStart, SpanEnd, Support)
Provenance(Extractor, ModelId?, ModelRevision?, PromptVersion?, RunId, At)
```

A document's identity **is** its content hash: a claim can never point at text
that changed under it. When a description changes, it is a new document, and the
claims resting on the old one are **superseded**, not edited.

**3 · The status is a table** (invariant 5, `TransitionTable<ClaimStatus>`):
`Proposed → Approved`, `Proposed → Rejected`, `Approved → Superseded`,
`Proposed → Superseded`. Approval and rejection are commands with an audit row
and a person behind them (invariant 12); a chat message is never an approval.

**4 · What code checks before a claim is ever proposed** — every rule measured
failing in `P10-0`:
- the quote must occur in the document (located case-insensitively, stored with
  the document's own spelling and span); otherwise the claim is dropped and
  counted;
- a value must parse against its attribute's kind, unit and options —
  `MATERIAL = WOOD` is refused, not stored;
- both statements must be in their language (1 in 6 were not, at 4 bits);
- a claim duplicating one already proposed for the same span is merged.

**5 · Support is measured, not declared.** `Evidence.Support` comes from the
support port — an NLI classifier today — never from a number the extractor writes
about itself, which is not a probability. The review screen shows a claim's
**weakest** evidence: a confident claim on a thin quote is the failure worth
surfacing.

**6 · What reads and what scores, as measured** (`P10-0`, decided 2026-09-27):
`IClaimExtractor` with **Qwen3.5-4B** (8-bit GGUF converted from its revision,
served by llama.cpp) and **the rules** as the path with no model (invariant 8);
`ISupportScorer` with **`bge-m3-zeroshot-v2.0` on TEI**. Every weight is pinned
by revision and file hash (ADR 0031); the server's settings are pinned with it,
because the same file gave different claims under two configurations.
Extraction runs in a worker, never in a request.

**7 · Only approved claims leave the context.** The product page, comparisons,
the MCP and UCP surfaces and the future assistant read approved claims, each with
its quote and who approved it. A comparison says *sin dato* where no approved
claim exists — enforced by the query, not by a prompt.

**8 · The gate.** `SearchEval --suite claims` and `--suite support` run the golden
sets through the ports that ship. They enter CI with thresholds in `P10-9`; a
change to the prompt, the model or the server settings that lowers them does not
merge.

## Consequences

- A sixth context, with its first writer in `P10-2`; the architecture rules
  already count it.
- A second model server beside TEI, with its limits sized from a measurement
  (~5 GB for the extractor), and ~30 s per product on CPU — a worker's pace.
- Every claim can be traced to a sentence, a model revision and a prompt version:
  when the prompt changes, the claims it produced can be found and re-proposed.
- The model can be wrong and the catalogue stay right. What a model decides
  wrongly reaches, at worst, a review queue.
- Rejected: letting the model write attribute values directly (the Shopify-app
  pattern of 2026, `analysis/tendero-suite-shopify.md` §2); trusting a model's
  self-reported confidence; a projection that a reindex could erase.
