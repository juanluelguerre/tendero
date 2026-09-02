# Gap analysis

Every capability Tendero needs to be a credible 2026 commerce platform, scored
**Done / Partial / Missing** against the code, not against the plan. Companion to
[`current-state.md`](current-state.md); the answer to each gap lives in
[`roadmap.md`](roadmap.md).

A capability is **Done** only if something calls it end to end. An aggregate that
compiles and that nothing invokes is **Partial** at best — `Order` is the
cautionary example and it is scored accordingly.

Verified against a clean build (0 warnings) and 117 passing tests on 2026-09-02.

---

## A · The mandatory floor

| Capability | Verdict | Evidence |
|---|---|---|
| Catalogue with variants | **Done** | Closed 2026-09-02 (phase 1). `Variant` is a child entity with SKU, price, axis values and tax class, in its own table; every product gets an implicit `{externalId}-DEFAULT` variant at import, so everything purchasable is a variant. The index carries one document per (variant, culture), collapsed to products at query time (ADR 0015). |
| Structured attributes | **Done** | Closed 2026-09-02 (phase 2). `AttributeDefinition` with kind, unit, options and source aliases; `Product.Attributes` is a typed `AttributeValue[]`. `SetAttribute("colour", "banana")` is now a rejection. |
| Localized attribute values | **Done** | Closed 2026-09-02 (phase 2), and measured: the catalogue stores the option code `NAVY_BLUE` and the index renders "azul marino" / "navy blue". English NDCG@10 0.720 → 0.811 with Spanish unmoved, which was the regression test. The two queries this cost went from 0.000 to 1.000. |
| Category taxonomy | **Done** | Closed 2026-09-02 (phase 2). `Category` with a localized name, a parent and a materialized path; the document carries `categoryCode` as a keyword for filters and `categoryPathText` as analysed text over the **whole branch**. es 0.860 → 0.943, en 0.811 → 0.937. |
| Prices | **Done** | ~~`Money` has only `+` and `*int`.~~ Closed 2026-09-02: `Money` grew subtraction, decimal multiplication, division, comparison, `Percent`, a named `Rounding` policy and largest-remainder `Allocate`. `src/Pricing` carries `PriceList` + entries keyed on SKU, two lists (`retail`, `vip`), validity windows and a segment resolved from the principal. |
| Promotions with combination rules | **Done** | Closed 2026-09-02. Four effects (`PercentOffLine`, `AmountOffOrder`, `BuyXGetY`, `FreeShipping`), coupons, segments and validity windows; combination as a declarative table with three policies, evaluated in the total order `(Priority, Code)`. Suppressed promotions come back with a localized `RuleReason`. Seven CsCheck properties at 2,000 carts each — one of which found a real bug. |
| Real-time inventory | **Missing** | No stock, no warehouse, no reservation, no availability. `InStock` does not exist in the search document. |
| Cart | **Missing** | No cart aggregate, table, endpoint or service. The storefront translation key `product.addToCart` exists in both languages and is referenced **zero** times. |
| Checkout | **Missing** | No command, no endpoint, no handler. `Order.Place` is called only from `tests/Ordering.Tests/OrderBuilder.cs`. |
| Taxes | **Done** | Closed 2026-09-02. `ITaxCalculator` with two keyed adapters (`flat-vat`, `zero`) and a shared contract suite; per-class rates, a breakdown grouped one line per rate, and shipping taxed as one more base. Destination rules are a declared simplification — the field already travels, so it is a third adapter and not an `if`. |
| Shipping | **Missing** | No address, no shipping option, no rate provider. `Order` has no destination of any kind. |
| Order state machine | **Partial** | The best-built thing that nothing uses. `AllowedTransitions` is a real declarative table with 7 states, 12 edges and full test coverage of the matrix — and **no production code calls a single transition**. |
| Returns / RMA | **Missing** | No return states, no `ReturnRequest`, no reason codes, no refund. The `OrderDelivered` event exists and its comment says it is there to open the return window; nothing consumes it. |
| Accounts and guests | **Missing** | `CustomerId` is a record struct with no entity behind it. No customer table, no guest identity, no session. |
| Idempotent payments with webhooks | **Missing** | `Order.IdempotencyKey` exists with a unique index — that is the whole of it. No `IPaymentProvider` (despite ADR 0003 describing it in the present tense), no adapter, no webhook endpoint, no signature verification. |
| Faceted search | **Partial** | BM25 is solid: per-language analysers, `cross_fields` + fuzzy `best_fields`, sensible boosts. But **no aggregations, no filters beyond `status: active`, no sorting, no price range, no facet counts**. Paging is `from`/`size` only. |
| SEO | **Partial** | Per-culture slugs are generated and stored, which is the hard half. Nothing else: no SSR or prerender (`@angular/ssr` is not installed), no meta or Open Graph tags beyond `viewport`, no `hreflang`, no sitemap, no structured data. The storefront is a client-rendered SPA on one route. |
| Backoffice with roles | **Partial** | Closed on the auth side 2026-09-02 (phase 0): JWT validation, three policies, and every endpoint carrying a policy or an explicit `AllowAnonymous`, enforced in both directions by a test. The backoffice has a login screen, a token interceptor and a route guard. Still Partial because the issuer is the development one and the roles live in claims rather than in a domain — `Accounts` lands in phase 7. |
| Audit | **Missing** | No audit table, no actor recorded on any command. The dispatcher has one pipeline step and it is validation. |
| Observability | **Partial** | Genuinely good instrumentation, no persistence. `ActivitySource` spans with tags in every I/O slice, an outbox-lag histogram, OTel wired from the first slice. But the OTLP exporter only activates if `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and **the AppHost declares no backend** — traces live in the Aspire dashboard's memory and die with it. |

**Floor summary (2026-09-02, after phase 3): 7 Done, 5 Partial, 8 Missing** — of 20.

Phases 1–3 closed variants, structured localized attributes, the taxonomy,
prices, promotions and taxes. What the floor is still missing is everything
transactional — inventory, cart, checkout, payments, returns — plus accounts and
audit.

(The pre-phase counts in earlier revisions of this file read "3 Done, 6 Partial,
15 Missing" against the same twenty rows. They did not add up; these are counted
from the table.)

The honest reading is that the floor is one bounded context (`Catalog`) plus half
of a second. The pieces that exist are well made; the commerce plane the project
claims to own — cart, checkout, orders, payments — does not run.

---

## B · Standard 2026 AI

| Capability | Verdict | Evidence |
|---|---|---|
| Hybrid search (lexical + vector) | **Missing** | No embedding, vector, Qdrant, RRF or rerank code. A repo-wide grep for `Qdrant\|dense_vector\|knn\|embedding\|rrf` returns only prose comments and two golden-set annotations. |
| Visual search | **Missing** | No image model, no ONNX runtime, no image embedding. |
| Multimodal product ingestion | **Missing** | Images *are* ingested as bytes and content-addressed (ADR 0011), which is the prerequisite — the deciding argument in that ADR was that "multimodal search cannot be built on images we do not control". Nothing consumes them beyond serving. |
| Conversational assistant | **Missing** | No chat, no agent, no tool calling. |
| Personalization | **Missing** | No user, no session, no event stream, no signal of any kind. |

**Zero AI code exists.** Not `Microsoft.Extensions.AI`, not an `IChatClient`, not
an `IEmbeddingGenerator`; no Ollama, no model, no prompt, no Python sidecar.
ADR 0005 decides the AI layering policy in full and has no corresponding line of
code.

What *does* exist is the set of seams the AI layer will attach to, and they are
well placed: `Product.Localize` and `LocalizedText.With` for enrichment and
translation; `IProductIndexer` and `ProductIndexProjection` for a second
projection; the outbox for driving any of it off a domain event.

---

## C · The differentiators

| Capability | Verdict | Evidence |
|---|---|---|
| **1 · Agent-native merchant** — `/.well-known/ucp`, UCP capabilities, MCP server | **Missing** | `src/Ucp/` is a directory holding one 65-byte README, with no `.csproj`, absent from `Tendero.slnx`. No `/.well-known/` route exists. No MCP package, no `MapMcp`, no tool definitions. |
| **2 · Product reasoning layer** — specs, evidence, comparisons with source and confidence | **Missing**, but the hard half is built | No claim, evidence, source-document or confidence type exists. However the **human-in-the-loop pattern already ships and is measured**: the backoffice review queue lists drafts, publishes them, and deliberately reloads from the server instead of updating optimistically because the outbox means the index lags. That is the interface this differentiator needs, already proven. |
| **3 · Know Your Agent** — signatures, AP2 mandates, agent-aware anti-fraud | **Missing** | No agent principal, no mandate, no signature verification, no policy. There is no authentication to build on. |
| **4 · Backoffice copilot** — natural language over own data and traces, with approvable actions | **Missing** | No copilot. Also missing is what it would read: there is no persistent trace store and no audit table. The `nav.agents` translation key exists in both languages and routes nowhere. |
| **5 · Explainable, constrained recommendations** | **Missing** | No recommender, no solver, no constraint model. |
| **6 · AI observability and evals as a feature** | **Partial** | This is the differentiator Tendero is furthest along on, and it does not look like it. `tools/SearchEval` is a real offline gate — NDCG@10 and recall@50 per culture, thresholds committed in `eval.thresholds.json`, non-zero exit under threshold, and two hard-won reproducibility fixes (drop the indexes; refresh explicitly). What is missing is everything about *AI* specifically: no cost, no token count, no latency per operation, no per-decision trace, no eval for anything but lexical relevance. And **nothing runs the gate automatically**, because there is no CI. |

**Differentiator summary: 0 Done, 1 Partial, 5 Missing.**

Three qualifiers matter more than the scores:

1. **The evaluation discipline is the real asset.** Most projects that reach for AI features have no way to tell whether they helped. Tendero has a measured, reproducible, committed baseline before it has a single AI feature — which is the correct order and is rare enough to be the headline of an article.
2. **The human-in-the-loop UI is already built.** Differentiator 2 inherits a proven review interface instead of inventing one.
3. **The agent-native claim is entirely unbuilt.** `initial-plan.md` calls the absence of a .NET UCP reference implementation "Tendero's headline contribution". Today the contribution is a README.

---

## What the frontend already admits

Four translation keys exist in Spanish and English, in both apps, and are used
**zero** times:

| Key | What it implies |
|---|---|
| `product.addToCart` | Cart, PDP, checkout |
| `nav.orders` | Order management in the backoffice |
| `nav.agents` | The agent activity panel |
| `nav.search` | The search evaluation panel |

Somebody wrote the labels for four features that do not exist. That is this gap
analysis, written in `i18n` by the repository itself, and it is a good sign: the
shape of the product is understood, it is just not built.

---

## The three known search failures, and what each one costs

From `docs/search-evaluation.md`, queries scoring **0.000** and already annotated
as diagnosis rather than noise:

| Failure | Cause | Fixed by |
|---|---|---|
| "navy blue shoes", "womens running shoes" | Attribute values stored only in Spanish | Localized attribute values (A) |
| "induction cookware" | `COOKWARE` is a code, not vocabulary anyone types | Localized taxonomy (A) |
| "reading light with usb", "ropa deportiva", "menaje de cocina", "gift for the kitchen" | Vocabulary gaps lexical retrieval cannot bridge | Hybrid search (B) |

This is the most useful table in the document, because it converts two floor
items and one AI item into a **measurable** before/after against a committed
baseline. The first two are model changes that should move the English score with
the Spanish score untouched — and if Spanish moves, the change broke something.
