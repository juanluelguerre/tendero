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
| Real-time inventory | **Done** | Closed 2026-09-03 (phase 4). `src/Inventory` with two warehouses, `StockItem(Sku, WarehouseCode)`, and `Reservation` with its own transition table. `IStockLedger` and two keyed allocation strategies with a shared contract suite; the process runs on the existing outbox rather than a saga framework (ADR 0024), compensation included, verified end to end against a real Postgres. `inStock` is on the search document and on the hit, and a count typed in the backoffice reaches the storefront card by the same path an order takes. |
| Cart | **Done** | Closed 2026-09-03 (phase 5). An aggregate inside `Ordering`, addressed by a 256-bit token rather than by its id, with guests first class. It carries **no money**: every figure comes from a live quote (ADR 0016), and the storefront shows suppressed promotions with their reason. `product.addToCart` is finally used. |
| Checkout | **Done** | Closed 2026-09-03. Quote revalidated against the same engine that issued it, idempotency checked before anything is charged, payment authorised **before** the order exists (ADR 0025), and the cart closed in one transaction with the events that drive the saga. A price that moved comes back as a 409 carrying the new quote. |
| Taxes | **Done** | Closed 2026-09-02. `ITaxCalculator` with two keyed adapters (`flat-vat`, `zero`) and a shared contract suite; per-class rates, a breakdown grouped one line per rate, and shipping taxed as one more base. Destination rules are a declared simplification — the field already travels, so it is a third adapter and not an `if`. |
| Shipping | **Done** | Closed 2026-09-03. `Address` in the SharedKernel, frozen onto the order; `IShippingRateProvider` with `flat-rate` and `zone-rate` behind a shared contract suite. A country nobody ships to is an empty answer and not an exception, which is what lets checkout say "we do not deliver there yet". |
| Order state machine | **Done** | Closed 2026-09-03, and finished on 2026-09-04: a stopped order now **carries why**. The reason used to travel in `OrderCancelled` and die in a processed outbox row, so an order the stock saga refused said `Cancelled` and nothing else — on the shopper's page, in the history and in the backoffice. `OrderStop` is a code the interface translates plus a detail carrying the SKU and the numbers, in the shape `AppliedDiscount` established, and a frontend test reads the codes out of `Order.cs` so a fourth one cannot ship untranslated. Closed 2026-09-03. Every transition now has a caller: checkout places and authorises, the saga confirms and cancels, the backoffice ships and delivers, the webhook fails a payment. The screen derives its buttons from the status and shows the server's refusal when it guesses wrong — the table stays the single source of truth. |
| Returns / RMA | **Done** | Closed 2026-09-03. `ReturnRequest` as its own aggregate because returns are per LINE, with a six-state table, a closed set of five reasons, and the window opening on `OrderDelivered` — cashing a design that event's own comment made in phase 1. Receiving restocks through the ledger, damaged goods do not, and the refund carries the returned lines' share of the discount and tax. |
| Accounts and guests | **Partial** | `src/Accounts` ships (2026-09-04). **A guest is a `Customer` with no `Subject`, not the absence of one** — an order placed without an account still belongs to a person, and modelling that person as null spreads the special case into every screen. Signing in has three outcomes and the domain tells them apart: registered, a guest LINKED (keeping its id, so yesterday's order stays theirs), or an existing account that ABSORBS the guest — superseded, never deleted, because orders still name that id. It is two requests in two contexts, because Ordering will need Accounts for order history and a dependency the other way would close a cycle. The storefront has an account page with order history — `GET /api/orders/mine`, a separate slice from the shopkeeper's list because that one may return everything and this one must never. Still Partial: the issuer is still the development one (`P7-5`…`P7-8`). |
| Idempotent payments with webhooks | **Done** | Closed 2026-09-03. `IPaymentProvider` with four operations — authorise, capture, refund and **void** — one adapter and the contract suite ADR 0003 described in the present tense. The webhook verifies an HMAC over the raw body with a five-minute tolerance and tells forging, replaying and "none of our business" apart. |
| Faceted search | **Partial** | BM25 is solid: per-language analysers, `cross_fields` + fuzzy `best_fields`, sensible boosts. But **no aggregations, no filters beyond `status: active`, no sorting, no price range, no facet counts**. Paging is `from`/`size` only. |
| SEO | **Partial** | Half of `P5-13` closed on 2026-09-03: the query is in the URL, so a result page is shareable, bookmarkable and survives the back button. The **locale is still not**, so one route serves two languages, and the per-culture slugs are still used nowhere because there is no PDP. Nothing else either: no SSR or prerender (`@angular/ssr` is not installed), no meta or Open Graph tags beyond `viewport`, no `hreflang`, no sitemap, no structured data. |
| Backoffice with roles | **Partial** | Closed on the auth side 2026-09-02 (phase 0): JWT validation, three policies, and every endpoint carrying a policy or an explicit `AllowAnonymous`, enforced in both directions by a test. The backoffice has a login screen, a token interceptor and a route guard. Still Partial because the issuer is the development one and the roles live in claims rather than in a domain — `Accounts` lands in phase 7. |
| Audit | **Partial** | The WRITER ships (2026-09-03, phase 7). A dispatcher decorator records every command with who acted, on whose behalf, the outcome and the trace id — on its own connection, outside the command's transaction, because a row inside it disappears exactly when the command fails (ADR 0027). `Denied` is its own outcome, separate from `Failed`, because a rule refusing is the system working. Credentials are redacted: the guest claim and checkout both carry a cart token. The backoffice screen ships too, and it **opens on the refusals**: a log whose default view is a thousand successful publishes is a changelog. Still Partial only because the agent activity panel (phase 11) and the copilot (phase 12) are its other two readers. |
| Observability | **Partial** | Genuinely good instrumentation, no persistence. `ActivitySource` spans with tags in every I/O slice, an outbox-lag histogram, OTel wired from the first slice. But the OTLP exporter only activates if `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and **the AppHost declares no backend** — traces live in the Aspire dashboard's memory and die with it. |

**Floor summary (2026-09-03, mid phase 7): 14 Done, 6 Partial, 0 Missing** — of 20.

Phases 1–5 closed the whole transactional half. The one thing still **Missing**
is audit; the five **Partial** rows are faceted search (no aggregations), SEO
(no locale in the URL, no SSR, no PDP), accounts (guests yes, customers no),
the backoffice's roles (claims, not a domain) and observability (instrumented,
no persistent backend).

(The pre-phase counts in earlier revisions of this file read "3 Done, 6 Partial,
15 Missing" against the same twenty rows. They did not add up; these are counted
from the table.)

The honest reading after phase 5 is that the shop works. A guest fills a basket,
is quoted live, pays, and gets an order that holds stock, captures on shipping
and gives the goods back on a return — every crossing between the five contexts
running over the outbox that phase 1 built.

What is left in the floor is not transactional. It is **audit**, which nothing
writes yet and three later features read; **accounts**, which is the other half
of the guest work; and the parts of SEO and faceted search that need screens
nobody has built — a product detail page above all, which phase 1 deferred to
"the cart phase" and the cart phase did not reach.

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
| **1 · Agent-native merchant** — `/.well-known/ucp`, UCP capabilities, MCP server | **Partial** | **The first of the three surfaces ships (2026-09-03, phase 6).** WebMCP: four tools registered on `navigator.modelContext`, calling the same Angular services the interface calls — a rule an eslint boundary enforces rather than review. The agent **inherits** the shopper's session, so it needs no principal, no token and no mandate, which is what makes this surface small and phase 11 large. Degradation is the feature detection itself: without the capability nothing registers and the shop is unchanged, tested in jsdom and in a browser. Still Partial because the other two surfaces — the MCP server and UCP — are phases 9 and 11. |
| **2 · Product reasoning layer** — specs, evidence, comparisons with source and confidence | **Missing**, but the hard half is built | No claim, evidence, source-document or confidence type exists. However the **human-in-the-loop pattern already ships and is measured**: the backoffice review queue lists drafts, publishes them, and deliberately reloads from the server instead of updating optimistically because the outbox means the index lags. That is the interface this differentiator needs, already proven. |
| **3 · Know Your Agent** — signatures, AP2 mandates, agent-aware anti-fraud | **Missing** | No agent principal, no mandate, no signature verification, no policy. There is no authentication to build on. |
| **4 · Backoffice copilot** — natural language over own data and traces, with approvable actions | **Missing** | No copilot. Also missing is what it would read: there is no persistent trace store and no audit table. The `nav.agents` translation key exists in both languages and routes nowhere. |
| **5 · Explainable, constrained recommendations** | **Missing** | No recommender, no solver, no constraint model. |
| **6 · AI observability and evals as a feature** | **Partial** | This is the differentiator Tendero is furthest along on, and it does not look like it. `tools/SearchEval` is a real offline gate — NDCG@10 and recall@50 per culture, thresholds committed in `eval.thresholds.json`, non-zero exit under threshold, and two hard-won reproducibility fixes (drop the indexes; refresh explicitly). What is missing is everything about *AI* specifically: no cost, no token count, no latency per operation, no per-decision trace, no eval for anything but lexical relevance. And **nothing runs the gate automatically**, because there is no CI. |

**Differentiator summary: 0 Done, 2 Partial, 4 Missing.**

Three qualifiers matter more than the scores:

1. **The evaluation discipline is the real asset.** Most projects that reach for AI features have no way to tell whether they helped. Tendero has a measured, reproducible, committed baseline before it has a single AI feature — which is the correct order and is rare enough to be the headline of an article.
2. **The human-in-the-loop UI is already built.** Differentiator 2 inherits a proven review interface instead of inventing one.
3. **The agent-native claim has its first surface.** WebMCP shipped on 2026-09-03, and it is the cheapest of the three by a wide margin precisely because of its trust model: the agent runs in the shopper's tab and inherits their session, so there is no principal to establish. The expensive two remain — the MCP server (phase 9) and UCP + AP2 (phase 11) — and `initial-plan.md`'s claim that there is no .NET reference implementation of UCP is still a README rather than a contribution.

---

## What the frontend already admits

Four translation keys exist in Spanish and English, in both apps, and are used
**zero** times:

| Key | What it implies | State |
|---|---|---|
| `product.addToCart` | Cart, PDP, checkout | **used** (2026-09-03) |
| `nav.orders` | Order management in the backoffice | **used** (2026-09-03) |
| `nav.agents` | The agent activity panel | still dead — phase 11. WebMCP shipped in phase 6 and needs no panel: an agent that inherits the shopper's session leaves no row an audit table would show. The panel is for the agents that have a principal of their own |
| `nav.search` | The search evaluation panel | still dead — phase 8 |

Somebody wrote the labels for four features that did not exist. Two of them do
now. The other two stay dead on purpose: they are this gap analysis written in
`i18n` by the repository itself, and deleting them would be deleting the note.

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
