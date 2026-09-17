# Glossary

The terms this repository uses without stopping to define them, defined once.

It is deliberately **not** a dictionary of software engineering. Everything here
is either a term of art from search relevance and agentic commerce that a
competent .NET developer has no particular reason to know, or a pattern this
codebase leans on hard enough that misreading it makes the code look strange.
Anything a reader can infer from the name is left out.

Every entry says what it means, why it is in this project, and where the code
that implements it lives. Numbers are the ones committed on 2026-09-06.

---

## Search quality

### BM25

**The formula that decides which product best matches the words somebody typed.**

Every product in the index is scored against the query, and the highest score
comes back first. Three factors multiply into that score:

1. **Term frequency, saturating.** Repeating a word raises the score, but the
   curve flattens fast — a description that says "zapatilla" fifty times is
   slightly better than one that says it once, not fifty times better. A linear
   curve is what keyword stuffing exploited in the 1990s.
2. **Inverse document frequency.** Rare words are worth far more than common
   ones. "de" appears in all hundred products and distinguishes nothing;
   "running" appears in three, so finding it says a great deal.
3. **Length normalization.** A five-hundred-word description mentioning
   "running" once is a coincidence; a ten-word one mentioning it is what the
   product is.

The name carries no meaning worth guessing at: **BM** is *Best Matching*, and
**25** is the iteration number — it was the twenty-fifth variant tried in the
Okapi experiments of the 1990s.

**The limitation is the important half:** BM25 does not understand anything. It
counts words. `regalo para casa` scored **0.305** against this catalogue on
2026-09-11, because no product contains the word "casa" — and no amount of tuning
reaches a word that is not there. Meaning is what reaches it — see *Semantic
search* below.

It never leaves, either. **Lexical search is the permanent fallback**
(CLAUDE.md invariant 8): if the model server or Qdrant are down, the shop still
searches.

→ `src/Search/Elasticsearch.cs` — two `multi_match` clauses under a `should`,
because `cross_fields` lets terms spread across fields and `best_fields` carries
the fuzziness that `cross_fields` cannot.

### NDCG@10

**A score from 0 to 1 on the *order* of the first ten results.**

BM25 produces a list; NDCG grades it. Three letters, three ideas:

- **G — gain.** A person annotated each product for each query beforehand:
  0 means irrelevant, 3 means exactly right. Gain grows as `2^relevance - 1`, so
  one perfect hit is worth far more than several mediocre ones.
- **D — discounted.** A hit in position 1 counts fully; the same hit in position
  9 counts for little, because nobody scrolls. The divisor is
  `log2(position + 2)`, zero-based — so position 1 is not discounted at all.
- **N — normalized.** The list's score is divided by the score of the best
  possible ordering of the same judgments, which puts every query on the same
  0-to-1 scale. **1.000 means it was impossible to order better.**

**@10** means only the first ten results are looked at. What sits below does not
matter, because the shopper does not get there.

**Worked end to end**, with the formula the code actually uses. Query
`cafetera`: the engine finds all four good products but puts a backpack first.

**What the engine returned**

| # | product | relevance | contribution |
|---:|---|---:|---:|
| 1 | Mochila urbana 20 L | 0 | 0.000 |
| 2 | Cafetera italiana 6 tazas | 3 | 4.417 |
| 3 | Cafetera de goteo 12 tazas | 3 | 3.500 |
| 4 | Molinillo de café manual | 2 | 1.292 |
| 5 | Taza de cerámica 350 ml | 1 | 0.387 |
| | | **DCG** | **9.595** |

**The ideal ordering** — the same products, better placed

| # | product | relevance | contribution |
|---:|---|---:|---:|
| 1 | Cafetera italiana 6 tazas | 3 | 7.000 |
| 2 | Cafetera de goteo 12 tazas | 3 | 4.417 |
| 3 | Molinillo de café manual | 2 | 1.500 |
| 4 | Taza de cerámica 350 ml | 1 | 0.431 |
| | | **ideal DCG** | **13.347** |

`9.595 ÷ 13.347 = ` **0.719**. Every good product was there. One intruder in
first place cost almost three tenths.

→ `tools/SearchEval/RelevanceMetrics.cs` — a pure function over
`(ranking, judgments)`, kept away from Elasticsearch on purpose and tested
against values worked out by hand, because a metric that is wrong invalidates
the gate in silence.

### recall@50

**Did the relevant products appear at all, in the first fifty?**

NDCG asks whether the ordering is right; recall asks whether retrieval found the
items in the first place. They are two different faults and both are published,
because the fix is different: **NDCG down means badly ordered, recall down means
not found.**

Recall was where this catalogue's weakness showed. It sat at 0.633 (es)
and 0.556 (en) because the golden set annotates products a shopper would
happily accept — a gym trainer under "running shoes", a duffel under
"backpack" — that a query demanding every word could not retrieve. It was the
query's `AND`, not the words: an OR fallback beside it took recall to 0.939 /
0.955 on 2026-09-11, with full matches still ranked first.

### Golden set

**The annotated answer key: queries, and the products a human says are the right
answers to them.**

60 queries per culture — 52 scored and 8 with nothing to find — against a hundred
products. Each query carries its Baymard query type, its split (`tune` or
`holdout`) and its counterpart in the other culture; the format and the reasons
are in [`search-evaluation.md`](search-evaluation.md).
Each judgment carries a `why` field, and that field is the point — an annotation
without a stated reason cannot be reviewed in a pull request, and the notebook
records one that was written from assumption rather than from the catalogue.

**Who built the pool matters as much as who wrote the grades.** Until 2026-09-15
it was completed from lexical search's own results, so anything only another
strategy found was unjudged and scored as irrelevant. It is now pooled from every
strategy's first ten, and `judged@10` in the report says how much of a strategy's
results the golden set can see at all.

Judgments are keyed on the **source id** (`externalId`), never on the internal
`ProductId`: that one is a GUID v7 minted on every import, so a golden set keyed
on it would expire each time the catalogue is reimported.

→ `tools/SearchEval/golden/{culture}.json`

### The quality gate

**The CI job that fails a pull request when relevance regresses.**

It drops the indexes, indexes the seed through the real ports, refreshes
explicitly, scores both cultures, and exits non-zero below the committed
thresholds. Both of those first steps were learned from wrong numbers: without
dropping, the previous run's documents compete in the ranking (0.674 then 0.360
from identical code); without an explicit refresh, the score depends on whether
Elasticsearch's once-a-second refresh landed before the query (0.860 then 0.769).

| culture | NDCG@10 | recall@50 | threshold |
|---|---:|---:|---|
| es | 0.692 | 0.738 | 0.67 / 0.72 |
| en | 0.748 | 0.795 | 0.73 / 0.78 |

**A quality number means nothing without the corpus it was taken on — or the
judgments it was scored against.** These replaced 0.921 / 0.921 later on
2026-09-15, when the golden set grew from 22 queries covering the question types
word matching answers to 52 covering eight types; the 22 still score 0.921. Those
replaced 0.927 / 0.935 the same day,
when the golden set stopped being pooled from lexical search alone: the same
engine scored lower because relevant products it never found started to count.
Those had replaced 0.813 / 0.766, scored before the golden set was completed and
the OR fallback added, which had replaced 0.943 / 0.937, measured over six
products. NDCG@10 over six documents
and over a hundred are different measurements — with six, nearly everything
retrieved is annotated and the metric only asks about ordering. Lowering the
threshold there was not a regression waved through; it was the ruler changing.

→ `tools/SearchEval/eval.thresholds.json` · the long version is in
[`search-evaluation.md`](search-evaluation.md).

---

## Search by meaning (phase 8)

Built in phase 8, and the default since 2026-09-17: every search asks the hybrid
strategy.

### Semantic search

**Finding things by what they mean rather than by the words they use.** It is the
umbrella for the next three entries: an *embedding* turns a query and a product
into points in one space, *vector search* finds the products nearest the query,
and a *reranker* re-reads the best few. Lexical search (BM25) is the other kind,
which counts words. **Hybrid search is semantic plus lexical**, merged into one
list — which is what Tendero runs.

It is here because words fail exactly where shoppers are vaguest: "regalo abuela"
names no product, and in Spanish the use-case queries went from 0.263 with
lexical search to 0.619 once meaning was added (`search-evaluation.md`).

→ `src/Search/Qdrant` (the vectors) · `src/Search/Strategies` (fusion and the
reranking stage)

### Embedding

**A list of numbers that stands for the meaning of a text or an image**, so that
things which mean similar things sit close together in that space. It is what
lets `gift for the kitchen` reach a saucepan that never contains the word
"gift". Tendero's model is `bge-m3` — one multilingual vector space serving both
cultures — served locally by Text Embeddings Inference at a pinned revision (ADR
0031), with the vectors in Qdrant.

### Hybrid search, and RRF

**Two rankings, one result list.** Lexical (BM25) and vector retrieval each
return a ranking, and **Reciprocal Rank Fusion** merges them using only each
item's *position* in its list, never its raw score — which is what makes the
merge possible at all, since a BM25 score and a cosine distance are not on a
comparable scale.

### Reranker

**A slower, better model that re-sorts the top few results** after retrieval has
narrowed the field. Too expensive to run over a whole catalogue, cheap over
twenty candidates. Ours is `bge-reranker-v2-m3`, a cross-encoder, over the first
twenty results with an 800 ms budget — and **only on a GPU**: on a CPU no reranker
tried could keep the budget and still help (ADR 0031). On the GPU it takes hybrid
from 0.810 / 0.783 to 0.843 / 0.831 (en / es).

→ `src/Search/Reranking` · the stage in `src/Search/Strategies/HybridSearchStrategy.cs`

### Cross-encoder

**A model that reads the query and a product together and scores the pair.** An
embedding model is the other kind, a *bi-encoder*: it turns the query and each
product into vectors separately, and only compares the vectors. Reading both at
once lets a cross-encoder see how the words of one relate to the words of the
other, so it judges better — and it has to run once per pair, so it is slow.

That is why it only sees the first twenty results: retrieval narrows a hundred
products (or a million) to a few, and the cross-encoder orders those. It is also
why its score is not an answer to "does the shop sell this": it measures how
close a product's text is to the query, and H5 showed it scores the right answer
to "regalo abuela" as low as a lamp for a phone.

→ `bge-reranker-v2-m3`, GPU only · `src/Search/Reranking`

### Query understanding

**Deciding what a query is about, before or beside retrieving anything.** Is it a
product type the shop sells, a product type it does not, or a question about the
shop itself? Retrieval cannot answer that — it always returns its nearest things —
so real shops answer it in a separate step. At scale the industry trains a
classifier from queries to product types: Amazon calls it Q2PT, and Instacart
describes a similar pipeline.

Tendero does it in two layers (ADR 0032). **Layer A**, with no model: whether any
product carries every word of the query in what it is, and whether the query asks
about a help topic with a page of its own (returns). **Layer B**, scope by
anchors: compare the query with the shop's categories and with categories it does
not sell. Layer B was measured and refuted — "gift for someone who works from
home" was told the shop does not sell stationery at every margin tried — and its
code is kept off, pending the author's decision.

→ `src/Search/Answers`

### RAG — Retrieval-Augmented Generation

**A generative model answers from context that retrieval found for it**, instead
of from what it remembers. The retrieval half picks the passages; the generation
half writes the answer from them.

**Tendero has the retrieval and not the generation.** Nothing in the repository
generates text today: there is no `IChatClient` and no prompt. So calling
today's search "RAG" would be wrong — it is semantic search. Generation arrives
with product claims (phase 10) and the backoffice copilot (phase 12), and when it
does, the retrieval it stands on is the one already measured in
`search-evaluation.md`.

### Graceful degradation

**Every AI feature has a non-AI path, and the degradation is tested** — not
asserted in a comment (CLAUDE.md invariant 8). Reranker times out, keep the
fusion; vector store down, drop that ranking and keep lexical. The test is the
invariant made executable: a hybrid strategy built with a *throwing* vector fake
returns byte-identical results to the lexical one, compared serialised on page
two, and tags the span `search.degraded` — because a silent fallback is
indistinguishable from bad relevance.

**Not configured is not degraded.** A machine without the vector layer or without
a GPU gets a named stand-in (`NoVectorLayer`, `NoReranker`), and the stage is
skipped with the tag `search.stage.unavailable` and no counter. Only a stage that
was configured and failed counts.

→ `tests/Search.Tests/HybridSearchStrategyTests.cs`

---

## Agent surfaces

Three of them, and **what separates them is the trust model, not the
transport.** They are not alternatives; each answers a different question.

| Surface | The agent is | How it is authorized | Phase |
|---|---|---|---|
| **WebMCP** | code in the shopper's own browser tab | **inherits** their session — no second principal, no token, no mandate | 6 ✅ |
| **MCP server** | an external process | anonymous by explicit decision; read-only | 9 |
| **UCP + AP2** | a principal of its own, server to server | bearer token *plus* a signed mandate | 11 |

### MCP — Model Context Protocol

**The standard way to hand tools to a language model.** Domain-agnostic: it says
how a tool is described and called, not what the tools are. Tendero's server
(phase 9) is a transport over the existing query dispatcher — a tool is about six
lines, and an architecture rule makes it a build failure for that project to
implement a handler of its own.

### WebMCP

**MCP inside the browser tab**, through `navigator.modelContext`. The agent runs
in the page the shopper already has open, so it **inherits their session** — the
reason this surface is cheap and phase 11 is expensive. Chrome-only and under
incubation at the W3C Web Machine Learning Community Group; that label travels
with the code rather than being discovered later by a reader.

The tools call **the same Angular services the interface calls** — enforced by an
eslint boundary, not by review: a tool file may not import `HttpClient`.

→ `frontend/apps/storefront/src/app/agent/storefront-tools.ts` ·
`frontend/libs/shared/agent`

### UCP — Universal Commerce Protocol

**A shop's capabilities published in a format machines can read**, at a fixed
address: `/.well-known/ucp`. Instead of an agent pretending to be a person with
a mouse — opening the page, finding the button, guessing — it reads the manifest
and knows how to search, look a product up, add to the cart, ask for shipping
options and check out. Launched at NRF in January 2026, backed by Shopify,
Google, Walmart, Target, Etsy and around twenty more.

**Why it matters here:** the official reference implementations exist in Python
and Node.js only. There is no .NET one. That gap is this project's headline
contribution.

The manifest will be **derived from a capability registry**, never hand-written,
so deleting a slice breaks the build rather than rotting the JSON.

→ `src/Ucp/` (a README until phase 9)

### AP2 — Agent Payments Protocol

**The signed permission slip an agent carries when it pays.** A cryptographic
**mandate** stating what it may spend, on what, and until when — "up to 200 €, in
these categories, until Friday" — verifiable and impossible to forge. Donated to
the FIDO Alliance in April 2026.

The interesting case is the refusal: an agent attempting a 340 € order is
**denied, with the mandate scope rendered beside the reason**. A system that says
yes is a demo like any other.

The verifier is keyed **by format version**, on purpose: the wire format is still
firming up, so a new format is a new adapter rather than a rewrite.

---

## Patterns the code assumes you know

### Outbox

**Domain events are written to a table in the same database transaction as the
state change that raised them, and a worker delivers them afterwards.**

The problem it solves is the one everybody hits eventually: saving a product and
then indexing it are two systems, and there is no transaction across both. Do it
inline and a crash between them leaves the database right and the index wrong,
forever, with nothing to notice. Writing the *intention* into the same
transaction makes the pair atomic — either both happened or neither did — and
delivery becomes a retry problem instead of a consistency problem.

The rule in this repository is absolute: **handlers never index, email or call a
model inside a request** (CLAUDE.md invariant 7). Raise the event; the worker
projects. Look at the flow diagram in article 00 and notice where the HTTP
response arrow sits — the request finishes before anything is indexed.

What it costs, and this is the part people forget: **the index lags.** That is
why the backoffice review queue reloads from the server after publishing instead
of updating optimistically, and why `tendero.outbox.lag` is a first-class metric
— it is the number that says whether eventual consistency is still eventual.

→ `src/Persistence/TenderoDbContext.cs` drains
`AggregateRoot.DomainEvents` into the outbox on `SaveChanges` ·
`src/Workers/OutboxProcessor.cs` polls every 2 s, batches 50, and stops retrying
a message at 5 attempts · a filtered index on `processed_at IS NULL` keeps the
drain query cheap.

### Saga, and compensation

**A business process that spans several contexts and cannot hold a database
transaction across them**, so each step is a message and every step that changed
something has an undo.

Placing an order holds stock; failing to hold it cancels the order; cancelling
gives back both the stock and the payment hold. **Compensation is the undo** — a
first-class path, not an error handler, and it is why a declined card leaves no
orphan reservation behind.

There is no saga framework here. Every arrow is an ordinary domain-event handler
running on the outbox above, **orchestrated from `Ordering`** (ADR 0024) — which
is worth knowing before reading the code, because the mechanism people expect to
find does not exist.

### Bounded context

**A boundary inside the model where a word means exactly one thing.** "Product"
means something different to a catalogue than to a warehouse, and forcing one
class to serve both is how a model rots.

The rule in this repo is stricter than the name suggests: **contexts share no
entities** (ADR 0014). `Catalog`, `Pricing`, `Inventory`, `Ordering` and
`Accounts` cross only by carrying values or an event record. Stock is keyed on a
**SKU string**, which is what lets Inventory never learn what a product is. An
architecture test computes the forbidden dependencies by reflection rather than
listing them by hand, so a clean context cannot pass the rule vacuously.

### Snapshot

**A crossing copies the values it needs and never holds a live reference.**

An order freezes the product's name and price at purchase time; an applied
discount freezes the promotion's code and label. Rename the product tomorrow and
last week's receipt still says what the customer actually bought. Same principle,
applied three more times: the variant label, the shipping rate, the verified
mandate (ADR 0002).

### Vertical slice

**One folder per feature — endpoint, validator, handler, contracts — instead of
one folder per technical layer.**

A slice may never reference another slice; shared behaviour goes *down* into the
kernel or *out* into a port. The payoff is that a feature is deleted by deleting
a folder, and read by reading one.

→ reference implementation: `src/Catalog/Features/ImportProducts`

### Port and adapter, and the contract suite

**A port is an interface the domain declares; an adapter is one implementation
of it**, registered as a keyed service so a new connector is a new class plus one
registration line, with no `if` in a handler.

The part that makes it real is the **contract test suite**: an abstract xUnit
class stating what any adapter must honour, which every adapter inherits. A new
adapter without its subclass does not merge. It is also what made the Keycloak
swap safe — and Keycloak did **not** pass unchanged, which was the finding: a
contract suite with a single implementation has not been tested as a contract.

### Transition table

**A state machine written as data**, so the legal moves are a table you can read
at a glance rather than a scatter of `if`s.

`Order`, `Cart`, `ReturnRequest` and `Reservation` each declare their edges on a
shared `TransitionTable<TStatus>`; the guard and the wording of the refusal live
once. **Never change a status outside `TransitionTo`** — the table is the single
source of truth, and the backoffice derives its buttons from it rather than
keeping a second copy.

→ `src/SharedKernel/TransitionTable.cs`

### Idempotency

**Doing the same operation twice has the same effect as doing it once.**

Not a nicety here: agents retry, webhooks are delivered more than once, and the
outbox guarantees *at least* once. Importing is keyed on
`(source, externalId)` via a unique index — that index *is* the idempotency key.
Checkout carries an explicit `IdempotencyKey`, because charging a card twice is
the failure nobody forgives. Publishing something already published reports it
and saves nothing.

### Human-in-the-loop

**A model proposes; a person approves; the system applies.**

The rule it exists to protect (CLAUDE.md invariant 12): **a model never writes to
an aggregate.** It writes a claim or a proposed command carrying its source,
confidence and provenance, and approval is a domain operation with an audit row
— never a chat message. Anything a model decides that could be decided
deterministically, is: the model parses and phrases, code chooses.

The interface for it already ships as the backoffice review queue, which is why
phase 10 inherits a proven screen instead of inventing one.

---

## See also

- [`search-evaluation.md`](search-evaluation.md) — the golden set format, the metrics, and every before/after number
- [`architecture.md`](architecture.md) — how the contexts, the outbox and the agent surfaces fit together
- [`adr/`](adr/) — the decisions behind most of the entries above
- [`delivery-plan.md`](delivery-plan.md) — what is built, what is next, and what was deferred with its measured number
