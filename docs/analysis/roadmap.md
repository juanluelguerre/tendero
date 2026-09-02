# Architecture roadmap

How every capability in [`gap-analysis.md`](gap-analysis.md) fits the codebase
that exists. Design only — no code was written for this document.

The execution order, task board and publication calendar live in
[`../delivery-plan.md`](../delivery-plan.md). This document answers *where does
it go and why*; that one answers *what do I do next*.

## Two rules that shape everything below

**Architectural completeness is total. Data volume is laboratory scale.** These
are different axes and conflating them produces bad design. If a promotions
engine needs real combination rules, it gets them. If checkout needs a saga with
compensation, it gets one. But the catalogue stays at six products, the warehouse
count stays at two, and the roles stay at three. Work that only exists because of
scale — outbox coalescing, bulk indexing, background reindex jobs, image
derivatives — is **deferred with its measured number recorded**, which is better
material than implementing it.

**Reuse the idioms the repository already earned.** Three of them carry most of
this design:

- The **declarative transition table** in `Order.AllowedTransitions` becomes the shape for promotion combination policies, stock reservations and returns.
- The **outbox is already the saga**. `OrderPlaced → StockReserved | StockRejected → Confirm | Cancel` needs no new mechanism, only new handlers.
- The **snapshot rule** from ADR 0002 applies three more times: a variant label, an applied discount and a payment mandate are all frozen strings on the order, never live references.

---

## The context map: two becomes six

| Context | Owns | New? |
|---|---|---|
| `Catalog` | products, variants, attribute definitions, categories, images | existing |
| `Pricing` | price lists, segments, promotions, tax calculation | **new** |
| `Inventory` | warehouses, stock, reservations | **new** |
| `Ordering` | carts, orders, returns, payments, shipping | existing, mostly empty |
| `Accounts` | customers, guests, agent principals, addresses | **new** |
| `Knowledge` | source documents, product claims, evidence | **new** |

Plus three modules that are **not** contexts because they own no state —
`Mcp` and `Ucp` are transports over existing slices, `Assist` holds copilot
proposals — and one cross-cutting `audit` schema.

This breaks the *title* of ADR 0002 ("Two bounded contexts") and the wording of
CLAUDE.md invariant 1. It does not break the principle. What ADR 0002 actually
protects is that contexts share no entities and communicate through events, and
that rule holds at six exactly as it held at two. **It needs a context-map ADR
that generalises the principle instead of the count.**

ADR 0007 needs widening too. It permits `Search → Catalog` and forbids the
reverse. Search must now also project stock (`Inventory`) and specs
(`Knowledge`), so the rule becomes: *Search may reference any context it
projects; no context may reference Search.* That is the invariant ADR 0007 was
protecting all along.

---

## A · The floor

### A1 · Variants

**Where** `src/Catalog` — `Variant` is a child entity of the `Product`
aggregate, not its own aggregate.

**Model** `Variant(VariantId, string Sku, Money Price, IReadOnlyDictionary<string,string> AxisValues, string? TaxClass, ImageId? Image, VariantStatus)`.
`Product` gains `Variants`, `VariantAxes` (ordered attribute codes) and a derived
price range. `Product.Price` **stays** as the default variant's price — removing
it would break the search document, the committed baseline and the storefront in
one commit.

A product with no variants gets **one implicit variant** minted at import
(`{externalId}-DEFAULT`), so every purchasable thing is a variant and `OrderLine`
has exactly one shape.

**The variant is the indexed unit; the product is the returned unit.** One
document per `(variant, culture)`, and every query collapses on `productId` with
`inner_hits` returning the variant that actually matched. This is the
load-bearing decision of the whole floor, and it is worth spelling out why
neither extreme wins.

*Indexing the product loses precision that a shop cannot afford.* Filtering
`colour = navy AND size = 38` would match a product carrying navy in a 40 and
black in a 38 — the classic false-positive facet, where the shopper clicks
through to a combination that does not exist. Price becomes a range, so "under
€30" matches products where merely *some* variant qualifies and sorting by price
is ambiguous. `inStock` degrades to "some variant is available", which is how a
card says available and the size you want is not. And an agent gets the worst of
it: UCP cart operations take a **SKU**, so a product-level result forces a second
call and a guess about which variant was meant.

*Indexing the variant with no collapse loses everything else.* A product with
eight variants floods the top ten, the golden set stops being comparable, and the
vector ranking — which is naturally product-level, because variants share the
name and description that get embedded — no longer fuses cleanly with the lexical
one.

*Collapse gets both.* Matching and filtering happen per variant, so facets are
exact. Results come back as products, so the golden set keyed on the product
`externalId` stays valid and the committed 0.860/0.720 baseline stays comparable.
`inner_hits` carries the winning variant, so the storefront card can render with
the matching colour already selected. And an agent chooses: collapsed to browse,
uncollapsed to add an exact SKU.

**The costs, stated plainly.** Facet counts count *documents*, not groups, so an
accurate product count needs a `cardinality` aggregation — exact at six products,
approximate at scale. `collapse` has rough edges with deep pagination. The index
multiplies by the variant count. And it is more complex than either extreme,
which is a real objection; it earns the complexity by being the only option that
does not sacrifice something the roadmap depends on later.

Document fields become variant-level (`sku`, `price`, axis codes, `inStock`)
plus the product fields that are shared (`productId`, name, description, brand,
category path), with the product's own aggregates — `priceFrom`, `priceTo` —
computed at query time from the collapsed group rather than denormalised.

**What changes elsewhere**

- `OrderLine` grows `VariantId`, `Sku` and `VariantLabel` — the resolved axis string ("Azul marino · 38"), snapshotted per ADR 0002.
- Variants get their own `ExternalReference` table with a unique `(source, external_id)`; the product's stays untouched.
- **Variants must be a table, not a `.ToJson()` complex collection.** They are queried by SKU (inventory, order lookup, UCP cart), filtered by price and joined by external reference. ADR 0008's own criterion — values that are genuinely queried become tables — already decides this. It is an amendment, not a contradiction.

**First slice** `Catalog/Features/DefineVariants`: take a product plus an axis
and option list, generate the matrix. `ListProducts` returns `variantCount` and
the price range.

### A2 · Structured, localized attributes

**Where** `src/Catalog` — new `AttributeDefinition` aggregate.

**Model** `AttributeDefinition(Code, LocalizedText Label, AttributeKind, string? Unit, bool IsVariantAxis, bool IsFacet, bool IsSearchable, AttributeOption[] (Code, LocalizedText Label))`
in a **table**, because facet building and the backoffice query it.
`Product.Attributes` becomes `AttributeValue[]` — typed, validated against the
definition, stored as **jsonb keyed by code**, because values are only ever read
with their aggregate.

`SetAttribute(string, string)` becomes `SetAttribute(AttributeValue)`, and an
unknown code or an option outside `AllowedValues` is a rejection. The
`OrdinalIgnoreCase` comparer trick in `src/Persistence/Jsonb.cs` disappears,
because codes are normalised in the domain.

**Migration path for the seed**: the connector mapper resolves raw source strings
against definitions and, when it cannot, falls back to a `Text` value plus a
**Draft definition proposed for review** — which is itself a review-queue item,
reusing the interface that already exists.

**This is the highest-value change in the floor**, because it is measurable.
`AttributesText` is rendered per culture from definition and option labels:

- es → `"color azul marino género mujer talla 38"`
- en → `"colour navy blue gender women size 38"`

Two of the three known 0.000 queries — "navy blue shoes" and "womens running
shoes" — become matchable. The honest experiment is to run the gate before and
after **with only this change**, and to publish both numbers. Spanish should not
move; if it does, the rendering broke something, and that is the regression test.

**Port** `IAttributeDefinitionReader` — needed by the connector mapper, the index
projection and the backoffice. Three consumers, so it is a port from birth per
CLAUDE.md invariant 2. The index projection needs a cache invalidated by an
`AttributeDefinitionChanged` event, or a reindex becomes N+1.

### A3 · Category as localized taxonomy

**Where** `src/Catalog` — new `Category` aggregate with a stable code id, a
`LocalizedText` name, a parent and a materialized path. Three levels, about eight
categories.

`Product.Category` (string) becomes `CategoryId?`. The column keeps its name and
its values, which matters while `EnsureCreatedAsync` is all there is.

**The search document gains two fields, and the split is the point.**
`categoryPathText` is analysed text, localized (`"Home Kitchen Cookware"`), and
joins the searchable fields at `^2`. `categoryCode` stays a `keyword` for filters
and facets. This reverses the removal recorded in `docs/search-evaluation.md` —
which was right *for a code* and wrong the moment the taxonomy carries labels.
It fixes the third known zero, "induction cookware".

Changing the analysed field list means the NDCG gate moves in the same PR, with
justification in the body, per CLAUDE.md's testing rules.

### A4 · Pricing and promotions

**Where** a **new context**, `src/Pricing`. Not `Catalog` — a price list is not a
product fact, and Catalog would grow a customer-segment concept it has no
business knowing. Not `Ordering` — the storefront must price a cart before an
order exists, which would force `Catalog → Ordering`, the wrong direction.
`Pricing` references `SharedKernel` only and takes product data as **input
values**, never as entities. That purity is what makes it property-testable.

**Model** `PriceList` + `PriceListEntry` (a table, unique on
`(price_list_id, variant_id)`; two lists at lab depth, `retail` and `vip`).
`Promotion(Code, LocalizedText Name, Condition, Effect, CombinationPolicy, Priority, ExclusivityGroup, validity window, usage limit)`
with condition and effect as jsonb.

**Four effect types**, each mechanically distinct: `PercentOffLine`,
`AmountOffOrder`, `BuyXGetY`, `FreeShipping`.

**Combination rules are a declarative table**, in the idiom `Order` already
established:

```
ExclusiveGlobal    → wins over everything below it, stops evaluation
ExclusiveInGroup   → suppresses later promotions sharing an ExclusivityGroup
Stackable          → applies and continues
```

Evaluation order is `(Priority, PromotionId)` — total and deterministic.

**Ports** `IPriceResolver`, `IPromotionEngine`, `ITaxCalculator` (keyed:
`flat-vat`, `zero`), plus repositories. The first three are **pure functions**:
values in, values out, with an injected `TimeProvider` for validity windows.

`PricedCart` carries
`AppliedDiscount(PromotionCode, LocalizedText Label, Money Amount, string RuleReason)`.
`RuleReason` is what makes a suppressed discount explainable in the UI, and it is
the difference between a promotions engine and magic.

**How this respects the snapshot rule.** Pricing produces a `PriceQuote` with an
expiry and a hash over its inputs. The storefront shows the live quote,
recomputed on every cart change; checkout **revalidates** and, on hash mismatch,
tells the shopper the price changed and asks them to reconfirm. `Order` then
freezes the quote as value objects and stores promotion **codes and labels as
strings** — exactly as it already stores `ProductName`. `Ordering` never
references a `Promotion` entity.

That mismatch path is worth noticing twice: it is the same mechanism as *"this
AP2 mandate does not cover this amount"* in C4. One piece, two features.

**`Money` must grow up.** It has `+` and `*int`. Percentage discounts, tax and
proportional allocation of an order-level discount across lines all need
division, a rounding policy and a residual-allocation rule. That is a
`SharedKernel` change and it belongs in this phase.

### A5 · Inventory

**Where** a **new context**, `src/Inventory`. It has its own lifecycle
(receipts, adjustments, reservations that expire), its own actor (a warehouse
operator) and its own consistency boundary per SKU — the three tests that
justify a context.

**Model** `Warehouse` (two rows), `StockItem(Sku, WarehouseId)` with `OnHand`,
`Reserved` and derived `Available`, and `Reservation` with **its own small
transition table** (`Held → Committed | Released | Expired`).

**`StockItem` is keyed on `Sku` (a string), not `VariantId`.** That is what keeps
Inventory from referencing Catalog at all: SKU is the shared vocabulary between
the two contexts, exactly as `ProductName` is between Catalog and Ordering.

**Ports** `IStockLedger` (reserve / commit / release / receive),
`IAllocationStrategy` (keyed: `priority-first`, `single-warehouse`, with a
contract suite), `IAvailabilityReader`.

**The saga already exists.** Every arrow below is an existing
`IDomainEventHandler<T>` plus the existing `OutboxProcessor`:

```
Ordering   Order.Place()          → OrderPlaced
Inventory  on OrderPlaced         → StockReserved | StockRejected(reason)
Ordering   on StockReserved       → order.Confirm()
Ordering   on StockRejected       → order.Cancel(reason)
Ordering   on OrderCancelled      → IStockLedger.ReleaseAsync   (compensation)
Ordering   on OrderShipped        → IStockLedger.CommitAsync
```

Each context references only its own domain plus the other's **event records**,
which is the one crossing ADR 0002 permits. Add an architecture rule:
`Inventory.*` must not depend on `Ordering.Domain`.

Stock reaches search through `StockLevelChanged → outbox → inStock` on the
document — which is the `Search → Inventory` edge that ADR 0007 has to widen for.

### A6 · Cart, checkout, taxes, shipping, returns

**Where** `src/Ordering`, which finally gets more than one file. **Cart is an
aggregate inside Ordering, not its own context**: cart-to-order is a single
transactional conversion, and splitting it buys a distributed saga for a state
transition. But `Cart` is emphatically not an `Order` and does not touch
`AllowedTransitions`.

**Model** `Cart` (nullable `CustomerId` for guests, a token, culture, currency,
lines, segment, last quote, expiry) with **lines in a table**, because UCP mutates
single lines and the agent activity panel queries them. `Address` as an owned
value object. `Order` gains shipping and billing addresses, a shipping option
snapshot, tax lines, applied discounts, the quote id and a nullable mandate
reference.

**Ports** `ICartRepository`, `IOrderRepository`, `IPaymentProvider` (keyed:
`fake` default with configurable failure, `stripe-mock`; authorize / capture /
refund), `IShippingRateProvider` (keyed: `flat-rate`, `weight-bands`). Each ships
the abstract contract suite ADR 0003 requires — and this is the first time ADR
0003's `IPaymentProvider` promise actually exists.

**Where tax and shipping live, and why they differ.** `ITaxCalculator` lives in
`Pricing`, because tax is a function of price, tax class and destination, and the
storefront must show tax-inclusive prices before checkout exists.
`IShippingRateProvider` lives in `Ordering`, because a rate needs a destination
address, and dragging `Address` into `Pricing` would ruin the purity that makes
`Pricing` testable. The shipping price then feeds *back* into `Pricing` as an
input for `FreeShipping`. Both directions are values, so the loop is fine.

**Returns are a separate aggregate.** The existing transition table has 7 states
and 12 edges and is readable at a glance; that legibility is a feature the repo
has already paid for. Adding `Returned`, `PartiallyReturned`, `RefundPending`,
`Refunded` and `RefundFailed` roughly doubles it — and worse, **returns are
per line**, which an order-level state machine cannot express without a
combinatorial explosion.

```
ReturnRequest(OrderId, ReturnLine[] (VariantId, Sku, Qty, ReturnReason, LocalizedText? Comment), Status, RefundAmount?)

Requested → [Approved, Rejected]
Approved  → [Received, Cancelled]
Received  → [Refunded, Rejected]
```

The window opens on `OrderDelivered` — which is not an invention. The comment on
that event in `src/Ordering/Domain/Order.cs` already says it exists precisely so
the phase-4 return-reason loop has something to hang from. This slice cashes a
design that was already made.

`ReturnReason` is a closed enum of five values. That enum is the input to the
return-reason analysis later, and the reason that analysis is possible at all.
Refunds go through `IPaymentProvider.RefundAsync`; restock goes through
`IStockLedger.ReceiveAsync` on `ReturnReceived`.

### A7 · Accounts, guests, roles, audit

Three separate things that get conflated.

**1 · Authentication: the issuer is a port, and the first adapter is a fake.**

**Nothing is anonymous.** Every endpoint has a policy from the first phase — the
public catalogue reads get an explicit `AllowAnonymous`, which is a decision
recorded in code rather than the absence of one.

The mistake to avoid is treating "add auth" as one large step that has to wait
for infrastructure. It is two steps, and the repo already owns the pattern:
**this is `FakePaymentProvider` applied to identity.** ADR 0003 says external
systems are reached through a port with keyed adapters and a shared contract
suite, and an identity provider is an external system.

The contract here is not an interface of ours — it is **OIDC**: a discovery
document, a JWKS endpoint, and signed JWTs. So:

| Stage | The issuer | Our code |
|---|---|---|
| **Now** | `dev-issuer` — a tiny in-process endpoint that serves `/.well-known/openid-configuration` and `/.well-known/jwks.json`, and mints signed tokens for seeded users and agents. Development only, refuses to start outside it. | `AddJwtBearer` pointed at it |
| **Later** | Keycloak in a container | `AddJwtBearer` pointed at Keycloak |

**The client half is real from day one and never changes.** Token validation,
`[Authorize]`, the four policies, `IPrincipalAccessor`, roles from claims, agent
tokens, the audit decorator — all of it is production code exercised from the
first phase. What is faked is only *who signs the token*, and swapping the signer
is configuration.

This is why it is not "writing an auth server". A dev issuer that mints tokens
for six seeded identities is a test fixture with an HTTP surface — roughly the
size of `FakePaymentProvider`, and with the same job: let the real path run
without standing up a dependency. **OpenIddict remains available** as a middle
option if a full OIDC server is ever wanted without Keycloak, but it should not
be reached for while the fake issuer is doing the job.

Keycloak still arrives, and it earns its place with the things a fake cannot
honestly provide: an admin console, real `client_credentials` for agents, and
**OAuth 2.0 Token Exchange** so an agent can act on behalf of a person in a token
carrying both `sub` and `agent_id`. It comes via the first-party Aspire
integration with a committed realm import, so a fresh clone starts with users,
roles and clients already created.

Inherited caution from ADR 0006, which rejected `Aspire.Hosting.Elasticsearch`
for declaring no licence and pinning a conflicting client: **verify the Keycloak
integration's licence and exact version against Aspire 13.5.3 before adding it.**
If it does not check out, the fallback is the pattern Elasticsearch already uses
here — a plain container resource.

The contract suite is what makes the swap safe: `IdentityProviderContractTests`
asserts discovery, JWKS rotation, expiry rejection, audience and issuer
validation, and role-claim mapping — and both adapters inherit it. A Keycloak
that fails it is a Keycloak that would have broken production.

**2 · `Accounts` owns the domain facts.** `Customer` with a nullable `Subject`
(the Keycloak `sub`; null means guest), display name, culture, currency,
addresses and segment. **Guests are first class**: a `CustomerId` is minted on
the first cart, and login runs `ClaimGuestAccount(cartToken, subject)` — a real
domain operation that links or merges. That is the `LinkExternal` idempotency
pattern from `Product`, applied to people: `subject` is to a customer what
`ExternalReference` is to a product.

Three roles: `shopper`, `shopkeeper`, `agent`. Held in Keycloak, mapped to
policies. The domain never stores a role.

**3 · Audit is a dispatcher decorator.** The pipeline already has one step
(validation); this is the second. `AuditEntry` records the command type and
payload, the customer, the agent, the mandate, the outcome (`Allowed`, `Denied`,
`Failed`), the denial rule, the trace id and the target ids.

**One writer, three readers**: the audit screen, the agent activity panel (C4)
and the backoffice copilot (B5). Getting it right once is why it is a decorator
rather than logging sprinkled through handlers.

---

## B · The AI layer

### B1 · Hybrid search

**Where** `src/Search`, new `Qdrant/` and `Reranking/` folders beside
`Elasticsearch/`.

**Ports** `ISearchStrategy` (keyed: `lexical`, `hybrid`), `IVectorProductSearch`,
`IReranker`, and a pure `ReciprocalRankFusion.Fuse`. **`ILexicalProductSearch`
does not change** — ADR 0004 said the public contract would not move, and it does
not.

**Both rankings are lists of products, and that is not an accident.** The lexical
side matches variants and collapses to products (A1); the vector side is
product-level, because variants share the name and description that get embedded
and a per-variant vector would let one product occupy eight nearest-neighbour
slots with near-identical content. So RRF fuses two rankings of the same unit,
and no reconciliation step is needed. Had the lexical side returned raw SKUs,
this is where that choice would have cost a whole extra layer.

**Degradation is staged, not all-or-nothing.** Reranker times out → keep RRF.
Vector store down → drop that ranking, keep lexical. Two budgets, both
configuration. The catch tags the span `search.degraded` and increments a
counter, because a silent fallback is indistinguishable from bad relevance.

**How ADR 0004's promise gets tested** — three levels, all cheap:

1. A `SearchStrategyContractTests` abstract suite every strategy inherits.
2. `HybridDegradesToLexical`: a hybrid strategy built with a throwing vector fake must return **byte-identical** results to the lexical strategy and must set `search.degraded`. This is the ADR made executable.
3. The gate: `--strategy lexical` must still clear 0.860/0.720 after hybrid ships, and hybrid gets its own higher thresholds. If hybrid ever scores below lexical, CI fails — which is the honest version of "AI improved search".

### B2 · Embeddings

**Where** `src/Search/Features/ProjectProductToVectors`, the same shape as the
existing `ProjectProductToIndex`, consuming the same `ProductUpserted` from the
same outbox. Ollama is reached through `Microsoft.Extensions.AI`'s
`IEmbeddingGenerator` **directly** — per ADR 0005, the MEAI abstraction *is* the
port, and wrapping it would add a layer that restates it.

**Model**: `bge-m3` — Apache-2.0, 100+ languages, one vector space serving both
cultures, dense and sparse from one model. Reranking: `bge-reranker-v2-m3`.

**Vectors live only in Qdrant.** Postgres is untouched; the vector index is a
disposable projection exactly like the Elasticsearch one, and
`POST /api/search/reindex` grows `?target=lexical|vector|all`.

**A mixed vector space is impossible by construction**, which matters because it
is the failure mode that produces plausible wrong answers:

1. The collection name carries the model identity — `products_bge-m3_v1_1024`. You cannot write a different model's vector into it, and the dimension check rejects it anyway.
2. Every point payload carries `modelId`, asserted on read.
3. A startup check compares the configured `EmbeddingModelDescriptor` against the collection metadata and, on mismatch, **marks the vector layer unavailable** — which degrades to lexical — rather than throwing. A model bump must never take the shop down.

Model change is a full rebuild into a new collection, then an alias flip. ADR
0012 already argues this ("the index is a disposable projection"); only the alias
is new.

### B3 · Multimodal ingestion

**SigLIP 2 in-process via ONNX Runtime. Not a Python sidecar.**

CLAUDE.md scopes the sidecar to evaluation and document ingestion; an image
embedding is a domain projection and would be the first crack in a rule the repo
has held cleanly. It is also outbox-driven batch work over a handful of images —
a sidecar would add a container, a network hop, a serialization format, a health
check and a failure mode for something that is a tensor in and a vector out.
`Microsoft.ML.OnnxRuntime` and `Microsoft.ML.Tokenizers` are MIT; SigLIP 2 is
Apache-2.0.

The image collection is keyed on `ImageId`, which is already a content hash — so
image embeddings deduplicate for free, exactly as image storage does, and an
unchanged image is a set difference rather than extra bookkeeping.

**Record explicitly** that SigLIP and SigLIP 2 spaces are not interchangeable —
which is precisely what B2's naming rule already prevents — and that **model
weights need the same per-version licence check as NuGet packages**. That is the
policy hole `current-state.md` §6 identifies.

**The known risk**, stated because it decides the approach: the text tower's
SentencePiece tokenizer must reproduce the reference tokenization exactly, or
text-to-image silently degrades. Mitigation is a fixture test against recorded
reference outputs, run *before* committing to in-process inference.

### B4 · The product reasoning layer

**Where** a **new context**, `src/Knowledge`. Not a `Catalog` extension: `Product`
would then have two writers — a shopkeeper editing and a model proposing — with
two lifecycles and two truth standards, which is how aggregates rot. Not a
projection: projections are rebuildable from truth, and a human's *approval* of a
claim is truth that exists nowhere else.

**Model**

```
SourceDocument(SourceDocumentId = content hash, SourceKind, Uri? Origin, Text, RetrievedAt)

ProductClaim(ClaimId, ProductId, ClaimKind, AttributeCode?, LocalizedText Statement,
             ClaimValue? TypedValue, double Confidence, Evidence[],
             Provenance(ModelId, ModelRevision, PromptVersion, RunId, At),
             Status { Proposed, Approved, Rejected, Superseded }, ReviewedBy, ReviewedAt)

Evidence(SourceDocumentId, string Quote, int? SpanStart, int? SpanEnd, double Support)
```

**Provenance is structural, not documentary.** The source document id *is* its
content hash, so a claim can never silently point at a document that changed. The
provenance record carries the model revision and prompt version, so when a prompt
changes you can find every claim it produced. Confidence sits on the claim and
support on each piece of evidence, and the display rule is that **a claim shows
its weakest evidence** — a confident claim resting on a thin quote is the failure
mode worth surfacing.

**The approval flow reuses the review queue.** Same pattern, different slice:
list `Proposed` claims by product with statement, confidence and the evidence
quote; `ApproveClaim` and `RejectClaim` are commands.

**`Knowledge` never writes to `Catalog`.** On approval it raises `ClaimApproved`
→ outbox → a `Catalog` handler calls `SetAttribute`, and a `Search` handler adds
it to a `specsText` field. The AI proposes, the human approves, the system
applies — three steps, each auditable. **The AI writes claims, not products.**

**Comparisons** return aligned rows of **Approved claims only**, and say "sin
dato" where a spec is unknown rather than guessing. That constraint is enforced
by the query, not by a prompt, which is the whole difference.

**First slice** `ProposeClaimsFromDescription` — extract structured specs from
the *existing* product description. No new sources, no scraping, no
infrastructure, immediate visible output.

### B5 · The backoffice copilot

**Where** `src/Assist`. This is where Microsoft Agent Framework is justified per
ADR 0005 — a real agent, tool-calling over read models, short-lived, no harness.

**Traces get two sinks, for two different readers.**

For humans: the **Grafana stack** (Grafana + Tempo + Prometheus) as AppHost
containers, which is what `initial-plan.md` §4 always intended. Grafana has been
AGPLv3 since 2021, and under the two-tier licensing policy that is fine: it runs
as a separate service over a network boundary, unmodified, so the copyleft never
reaches Tendero's code. Langfuse joins later for the LLM-specific view.

For the copilot: **not raw traces.** A trace store is the wrong interface for a
language model — unbounded cardinality, no semantics, and a large
prompt-injection surface. Instead a curated table written by a **MEAI delegating
client** that both emits the OTel GenAI span the conventions already require and
records a row:

```
telemetry.AiInvocations(TraceId, ConversationId, Operation, ModelId, PromptVersion,
                        InputTokens, OutputTokens, EstimatedCostMicros, LatencyMs,
                        Outcome, ErrorKind, At)
```

One instrumentation point, two destinations. The copilot's five tools —
`query_ai_usage`, `query_audit`, `query_outbox_health`, `query_search_quality`,
`query_catalog_gaps` — are ordinary `IQuery<T>` handlers over domain-shaped
tables.

**Proposed actions are serialized commands, not prose.**

```
ActionProposal(CommandType /* must resolve to a registered command */, CommandPayload,
               LocalizedText Rationale, Evidence[], Provenance,
               Status { Proposed, Approved, Rejected, Executed, Failed }, ReviewedBy, ExecutionResult)
```

Approving deserializes the payload and dispatches it through the existing
`ICommandDispatcher`. Three consequences, all good: the copilot's action
vocabulary **is** the registered command set, so it cannot invent an action; the
FluentValidation step still runs; and approval becomes an audit row rather than a
chat message. It also makes the copilot's eval a deterministic assertion instead
of an LLM judge.

### B6 · Evaluation and AI observability as a feature

`tools/SearchEval` is **extended, not replaced**. Its metrics, golden-set format,
index admin and the two hard-won reproducibility rules generalise; that is the
expensive part and it already exists.

Add a `--suite` switch with per-suite thresholds:

- `search` — unchanged, now per strategy.
- `claims` — a golden claim set; precision and recall of extraction. Deterministic on typed values, with a small pinned LLM-judge subset for phrasing.
- `copilot` — backoffice questions with an **expected command type and payload**. No judge needed; assert the proposal. This is the cheapest high-value eval in the project, and it exists only because B5 models actions as commands.

The `--suite` refactor adds no capability and **must land before hybrid search**,
or hybrid ships without a comparable baseline.

Separately, a backoffice **AI observability panel** over `AiInvocations`: cost
per conversation, p50/p95 latency per operation, tokens per model per day, and
the degradation counter from B1.

---

## C · Agent-native

Tendero ends up with **three** agent surfaces, and their trust models are
different. That difference is the interesting part, and no other commerce project
is likely to have all three.

| Surface | The agent is | Authorization | Depends on |
|---|---|---|---|
| **WebMCP** | code inside the user's own browser session | **inherits** the user's session, guest or signed in — no second principal, no token of its own, no mandate | a cart |
| **MCP server** | an external process | `AllowAnonymous` by explicit decision; read-only and public | nothing |
| **UCP + AP2** | its own principal, server to server | bearer token plus a signed mandate | auth, checkout |

### C1 · WebMCP

Chrome 146 shipped `navigator.modelContext` in February 2026 (W3C Web Machine
Learning Community Group, Google and Microsoft). **It is under incubation, not on
the standards track, and Chrome-only today** — it enters with that label attached.

**Where** `apps/storefront`. Per ADR 0010, the registration *mechanism* is
behaviour and goes to `libs/shared/agent`; the *tool set* is identity and stays
per app. The backoffice grows its own later, where it meets the copilot.

**The rule that keeps it honest**: tools call **the same Angular services the UI
calls** — never a parallel path. It is the frontend sibling of "MCP is a
transport over the query dispatcher".

**Degradation** is `if ('modelContext' in navigator)`. Without support the shop
works exactly as before — invariant 8 applied in the browser. Tool descriptors
live in one file, so a spec change is one edit.

### C2 · The read-only MCP server

**Where** a new project `src/Mcp`, **mounted in the API process** at `/mcp` via
`ModelContextProtocol.AspNetCore`. Separate assembly so architecture rules can
constrain it; same process so it shares the DI container and can reuse handlers.

Five tools: `search_products`, `get_product`, `list_categories`,
`get_product_specs`, `compare_products`. The last two are the differentiator —
they answer from approved, source-backed claims.

**Reuse is enforced, not encouraged.** Each tool is about six lines: build the
query record, call `IQueryDispatcher`, map to a flat result — never returning a
strongly-typed id raw, because CLAUDE.md's warning about `ProductId` serialising
as `{"value":"…"}` applies here verbatim. A new architecture rule makes
duplication a build failure:

> Types in `ElGuerre.Tendero.Mcp` must not implement `IQueryHandler<,>` or
> `ICommandHandler<,>`, and must not depend on any `*.Domain` namespace.

**Culture over MCP.** There is no `Accept-Language`, so ADR 0013's chain becomes:
explicit `culture` **tool parameter** (with the supported values enumerated in
its JSON Schema) → session default captured at `initialize` → `es`. The analogue
of `Content-Language` is data: every result carries `culture`, and
`missingCultures` travels per item — which ADR 0013 already prescribes. `Vary`
has no analogue and is not needed.

**Authorization**: the read tools are `AllowAnonymous` by explicit decision —
catalogue search is public on the web too, and an agent reading a public
catalogue needs no identity. That is a policy in code, not an absence of one, and
it is what lets the MCP server ship before Keycloak while everything else is
already behind a token from the fake issuer.

### C3 · The UCP manifest

**Where** `src/Ucp`, which becomes an actual project.

**The manifest is derived from a capability registry, never hand-written.**
Hand-written JSON rots the first time an endpoint changes and nothing catches it.
`IUcpCapability` exposes an id (`dev.ucp.shopping.checkout`), a version, its
service descriptors and a schema URI; capabilities are discovered by assembly
scanning, the same mechanism `AddTenderoCqrs` already uses. Each capability class
**names its slices by referencing their query and command types**, so deleting a
slice breaks the manifest at compile time.

| UCP capability | Slices |
|---|---|
| `dev.ucp.shopping.catalog.search` | `Search/SearchProducts` |
| `dev.ucp.shopping.catalog.lookup` | `Catalog/GetProduct`, `Knowledge/GetApprovedClaims` |
| `dev.ucp.shopping.cart` | `Ordering/AddToCart`, `UpdateCartLine`, `GetCart` |
| `dev.ucp.shopping.discount` | `Pricing/QuoteCart` |
| `dev.ucp.shopping.fulfillment` | `Ordering/GetShippingOptions` |
| `dev.ucp.shopping.checkout` | `Ordering/PlaceOrder` |
| `dev.ucp.shopping.orders` | `Ordering/GetOrder`, `ListOrders`, `RequestReturn` |
| `dev.ucp.identity` | `Accounts/LinkIdentity` |
| `com.elguerre.tendero.knowledge` | `Knowledge/CompareProducts` — the reverse-DNS extension mechanism, carrying the project's own differentiator |

**This is what forces OpenAPI.** Capability schema URIs are `$ref`s into a
committed document. Use `Microsoft.AspNetCore.OpenApi` — in-framework, MIT, no
new licence review — with build-time generation into `docs/openapi/tendero.json`.
Two things fall out: a **contract test** that the committed document equals the
runtime one (the repo's first API contract test), and **generated TypeScript
types** for `shared-api` via `openapi-typescript`, replacing the hand-written
mirrors while keeping hand-written clients per ADR 0010 — types are behaviour,
clients are identity.

**First slice**: `/.well-known/ucp` exposing only the two read capabilities that
already exist. A valid, honest, minimal manifest an agent can fetch — shipped
with the MCP server, long before checkout.

### C4 · Know Your Agent

**An agent is a principal, not a customer**, and both travel together.

```
AgentPrincipal(AgentId, Issuer, ClientId, PublicKeyJwkRef?, DisplayName,
               TrustLevel { Untrusted, Known, Verified }, Status, RegisteredAt)

CommercePrincipal(CustomerId Customer, AgentId? Agent, MandateReference? Mandate)
```

`CommercePrincipal` reaches commands through an `IPrincipalAccessor` port —
**not** ambient `HttpContext`, because it must work over MCP and inside the
outbox worker where there is no HTTP context. That constraint alone makes the
port non-negotiable.

**Mandate verification is a keyed adapter, versioned on purpose.**
`IMandateVerifier` exposes a `FormatVersion` and returns a
`MandateScope(MaxAmount, AllowedCategories, ExpiresAt, Subject, MerchantId)`.
AP2 was donated to the FIDO Alliance in April 2026 and its specification is
expected to firm up between H2 2026 and early 2027, so a new wire format is a new
adapter and a new key, with the shared contract suite catching semantic drift.
This is ADR 0003 doing exactly the job it was designed for.

The `fake` adapter — a self-signed JWS of our own — is the default and runs the
demo: full mechanism, zero external dependency. The verified mandate is
**snapshotted onto the order** and never re-resolved: ADR 0002's principle, third
application.

**Anti-fraud becomes agent-aware** through an `IAgentPolicy` evaluated in a
dispatcher decorator, at the same insertion point as validation and audit, with
rules as a declarative table: `RequiresValidMandate`, `OrderValueWithinMandate`,
`MaxOrdersPerAgentPerHour`. Each returns `Allow` or `Deny(rule, localized
reason)`, and **denials are recorded, not swallowed** — a denial is the most
interesting row in the system.

The **agent activity panel** is the `AuditEntry` table with agent columns: who
acted and on whose behalf, the mandate and its scope, the command attempted,
allowed or denied and which rule fired, the resulting cart or order, and the
trace id. That is the payoff for having one audit writer.

### C5 · Explainable, constrained recommendations

**Where** `src/Knowledge/Features/BuildKit`, because compatibility claims are
what make the answer explainable.

**The model never selects the items.** Four stages, and only the first is AI:

1. **Parse** — MEAI with structured output turns "un kit de cocina para dos por menos de 120 €" into a typed `KitRequest`. Fixture-tested. If parsing fails, the UI falls back to a form, so the deterministic path is always reachable.
2. **Retrieve** — the existing `ISearchStrategy` returns ≤ 20 candidates per slot. Ordinary search, already gated.
3. **Solve** — a **pure deterministic solver**: bounded branch-and-bound over ≤ 20 candidates × ≤ 5 slots, maximising relevance then coverage, subject to budget, stock and compatibility. At these sizes it is exact and instant.
4. **Explain** — generated **from the solver's own trace**: which constraint was binding, which candidate was displaced and by how much. A model may *phrase* that trace in es/en; it may not add facts.

Because `IKitSolver` is a pure function, the correctness argument is four
properties, verifiable with CsCheck: the total never exceeds the budget; every
required slot is filled or explicitly reported unsatisfied; adding a candidate
never worsens the objective; the solution is stable under input reordering. None
of those can be stated about a model that picks products.

---

## D · Cross-cutting

**Feature flags.** A closed `FeatureId` enum plus `IFeatureGate` over
`IOptionsMonitor`, about forty lines in `SharedKernel`. Not
`Microsoft.FeatureManagement` — MIT and therefore allowed, but it brings
targeting and variants that nothing here needs, and the dependency budget is
deliberately small.

This does not contradict article 07's argument against speculative flags: **ADR
0004 already mandates them**, and the degradation test in B1 cannot be written
without one. The discipline that keeps the rule intact is that the enum stays
closed and small (five: `SearchVector`, `SearchRerank`, `SearchMultimodal`,
`UcpTransactional`, `Copilot`), a test asserts every value has exactly one
production call site, and **each flag records its removal condition** — e.g.
`SearchVector` is deleted when hybrid beats lexical on both cultures for three
consecutive gate runs.

**Migrations.** ADR 0008 says none yet; **variants force the change immediately**.
`EnsureCreatedAsync` does nothing when the schema already exists, so the first
additive table leaves every existing developer database silently wrong — the same
class of failure ADR 0012 documented. Add EF Core Design, an initial migration
reproducing today's schema exactly, and `MigrateAsync` in the worker.
Testcontainers lands in the same PR, because a migration you cannot test is a
migration you cannot trust.

**OpenAPI** has two triggers and the earlier one wins: UCP needs it eventually,
but the floor adds roughly 25 endpoints while `shared-api` hand-mirrors DTOs with
no contract test, so drift is guaranteed and silent. It enters with the floor.

**Outbox at scale — deferred, with the number.** Measured: 47 rows for the
6-product seed, about eight `ProductUpserted` per product, projecting to ~1.2M
rows for the full 147k ABO set. The floor makes this *worse* (variant mutations,
claim approvals and price changes all raise events; one `ProductUpserted` now
drives two projections), which strengthens the eventual case without changing
today's priority.

The design is recorded so it can be executed in one sitting later: coalesce in
`DrainDomainEventsToOutbox` by `(aggregate id, event type)`, and **only for
events marked as state snapshots** — two `OrderPaymentFailed` with different
reasons in one unit of work are two facts, and collapsing them loses one. The
marker interface is not added now; adding it before the coalescing exists is
exactly the speculative move the flag rule forbids.

What *does* land, because it is nearly free and it is a screen: a **dead-letter
view** of messages that exhausted `MaxAttempts`, with their error and a retry
button. `SKIP LOCKED` and exponential backoff stay deferred — one replica, and
the comment in `OutboxProcessor` is still right.

**Testing tools, and when each becomes mandatory.** Each gets its licence checked
at the version added, per ADR 0006.

| Tool | Mandatory at | Why it cannot be avoided |
|---|---|---|
| **CI (GitHub Actions)** | before anything | The NDCG gate, the architecture rules and the OpenAPI contract test are theatre until something runs them on a PR. Highest-leverage missing piece in the repo. |
| **Testcontainers** (+ Respawn or our own TRUNCATE helper) | first migration | You cannot test a migration, a jsonb converter, a unique index or an outbox drain against an in-memory provider. |
| **CsCheck** | promotions | Combination rules are the canonical property-based case: totals never negative, stackable sets order-independent, exclusive promotions never co-apply. Reused for the kit solver. |
| **Verify** | UCP manifest | A derived manifest and a generated OpenAPI document are only safe if a change shows up in a diff. |
| **Playwright** | checkout | The demo *is* the deliverable, so the demo path must be the tested path. Two specs: storefront search→cart→checkout, backoffice login→review→publish. |
| **NSubstitute** | hold | The deterministic fakes are better and the contract suites cover the ports. Admit it when a MEAI abstraction genuinely needs stubbing. |

**`TimeProvider` now, not later.** Promotion validity windows, price-list
effective dates, mandate expiry, reservation expiry and return windows are all
time-bounded rules, and none is deterministically testable while `Product.Touch`
and `Order.TransitionTo` call `DateTimeOffset.UtcNow` directly. `TimeProvider` is
in the BCL, so it costs no licence review and no package. Two aggregates now;
eight later.

**One `TenderoDbContext`, six schemas.** The single context is what puts a domain
event and its state change in one transaction, which is the outbox's whole
correctness argument. Split the *configurations* per context and add an
architecture rule that a configuration may only reference its own context's
domain.

---

## ADRs this forces

| # | Decision |
|---|---|
| 0014 | Context map: contexts share no entities — the principle, not the count of two |
| 0015 | The variant is the indexed unit; the product is the returned unit (collapse) |
| 0016 | Prices are quoted live and frozen at order time |
| 0017 | The identity provider is a port; the first adapter is a development issuer |
| 0018 | The AI writes claims, not products |
| 0019 | The copilot proposes commands, never prose |
| 0020 | MCP and UCP are transports over the dispatcher |
| 0021 | The UCP manifest is derived from a capability registry |
| 0022 | An agent is a principal, not a customer |
| 0023 | Five feature flags, each with a removal condition |

Plus amendments to **0002** (title and count), **0003** (`IPaymentProvider` moves
from present tense to present fact), **0004** (the flag mechanism it assumed),
**0006** (new packages, and the licence policy extended to model weights and
container images), **0007** (Search may project any context), **0008**
(migrations; variants, cart lines and return lines as tables), **0010**
(frontend DTO types become generated), **0012** (a second rebuildable index) and
**0013** (culture negotiation over MCP).

---

## Open forks

The genuinely contested choices, each with a recommendation.

1. **Indexed unit: Product or Variant?** → *Resolved: variant documents, collapsed to products at query time.* The first version of this document recommended product-level roll-up, weighted mainly by not wanting to invalidate 22 golden-set annotations. That was the wrong weighting — a migration concern deciding a permanent property. Collapse keeps the baseline comparable *and* gives exact facets and direct SKUs for agents, at the price of `cardinality` aggregations for product counts.
2. **Where pricing lives?** → **Its own context.** Catalog would learn about customer segments; Ordering would force Catalog to depend on it. Purity is what makes the engine property-testable.
3. **Is Cart its own context?** → **No, an aggregate in Ordering.** Cart-to-order is one transactional conversion. Reconsider only under a scale argument, which is out of scope.
4. **Keycloak, or something lighter?** → *Resolved by staging.* A development issuer first, Keycloak later, both behind the same OIDC contract suite. The question stops being "which provider" and becomes "when does the fake stop being honest" — and the answer is when agents need real `client_credentials` and token exchange. OpenIddict remains the middle option if a real OIDC server is ever wanted in-process, but reaching for it while the fake issuer works would be building the thing the staging exists to avoid.
5. **Knowledge: context, extension or projection?** → **New context.** Human approval creates truth that cannot be rebuilt, which rules out a projection; two writers on `Product` rules out an extension.
6. **SigLIP 2 in-process or sidecar?** → **In-process**, with the tokenizer-fidelity fixture test as the gate on that decision.
7. **MCP in the API process or its own service?** → **In-process, separate assembly.** Sharing the container makes handler reuse free; the separate assembly makes the rule enforceable.
8. **Copilot: commands or prose?** → **Serialized commands.** It bounds the vocabulary to what exists, reuses validation, makes approval auditable and makes the eval deterministic. That it constrains the copilot to existing slices is the point.
9. **Trace backend.** → *Resolved.* The Grafana stack for humans, a curated `AiInvocations` table for the copilot. The licence objection was the blanket rule being too blunt, and the rule has been split in two: permissive for what we link, copyleft acceptable for what we run beside the app. What survives from the original argument is the *other* half, and it is the important one — **never let a language model query raw traces.** Unbounded cardinality, no semantics, and a large prompt-injection surface.
10. **A general `Result<T>`?** → **No.** Keep exceptions for invariant violations; the `SearchUnavailableException` + `IExceptionHandler` pattern already maps cleanly to ProblemDetails. Add per-context outcome records where several expected failures must be returned together. A repo-wide monad would touch every existing handler for a benefit only three new contexts need.
