# Delivery plan

The board. Open this to know what to do next; everything else is reference.

- **What exists today** → [`analysis/current-state.md`](analysis/current-state.md)
- **What is missing and why it matters** → [`analysis/gap-analysis.md`](analysis/gap-analysis.md)
- **Where each thing goes architecturally** → [`analysis/roadmap.md`](analysis/roadmap.md)
- **Per-article publishing metadata** (slug, excerpt, tags, blockers) → [`blog/index.md`](blog/index.md) — **that file stays the owner of it. This one does not duplicate it.**
- **Raw material, written as things happen** → [`blog/notebook.md`](blog/notebook.md)

---

## Where I left off

> Update these three lines at the end of every session. They are the point of the file.

- **Current phase:** 2 — **complete** (10/10)
- **Next task:** `P3-1`, `Money` grows division and a rounding policy — pricing
- **Next publication:** article 00 on **2026-09-15** — PNGs exported and committed; what remains is uploading them to the WordPress media library and swapping the four relative paths

**Decisions taken 2026-09-02** — 1 · an ADR generalises the context principle rather than fixing a count · 2 · nothing is anonymous; the identity provider is a port whose first adapter is a development issuer, Keycloak later · 3 · the variant is the indexed unit and the product the returned one, via `collapse` · 4 · licensing splits into two tiers, so the Grafana stack is back in · 5 · UCP is split, read capabilities in phase 9 and the transactional half in phase 11.

---

## How this is ordered

The sequence optimises for **what it evidences**, not for what is cheap. Nothing
is cut for being laborious. What *is* constrained is data volume: six products,
two warehouses, three roles. Scale work is deferred with its measured number
recorded, which is better material than building it.

Two hard dependencies drive everything: **auth unlocks the agent economy**
(UCP, mandates, the activity panel), and **cart/checkout unlocks everything
transactional**. Phase 0 comes first because the NDCG gate, the architecture
rules and the OpenAPI contract test are theatre until something runs them on a
pull request.

**Sizes are sequencing information, not a budget.** S fits in one session, M in a
few, L is a project. Use them to decide what fits tonight, never to decide what
to drop.

---

## Publication calendar

**Fortnightly, decoupled from shipping.** Write when a phase closes and the
notebook is warm; publish on the date regardless. This is the single most
important process decision in the file, because the failure mode of a developer
blog is not running out of material — it is publishing four things in a week and
then vanishing for two months.

You start with an unusual advantage: **eight finished drafts and one half-written**.
That is roughly four months of publishing without writing a new line, during
which phases 0–3 refill the queue.

| Date | Article | State |
|---|---|---|
| 2026-09-15 | 00 · From CocktailDev to Tendero | ready — PNGs exported; upload and swap the four paths |
| 2026-09-29 | 01 · One port, N catalogs | ready — needs a repo tag |
| 2026-10-13 | 02 · Search with regression tests: NDCG as a CI gate | ready |
| 2026-10-27 | 03 · The catalogue nobody could find | ready |
| 2026-11-10 | 04 · Your build is green and nothing works | ready |
| 2026-11-24 | 05 · The parameter and the header are not alternatives | ready |
| 2026-12-08 | 06 · A repository that built on exactly one machine | ready |
| 2026-12-22 | 07 · The feature flag you did not need | ready — short on purpose, do not pad |
| 2027-01-05 | **NEW** · from phase 0 | write when P0 closes |
| 2027-01-19 | **NEW** · from phase 2 | write when P2 closes |
| 2027-02-02 | **NEW** · from phase 3 | write when P3 closes |
| … | one per closed phase, in order | |

One standing blocker before 2026-09-29: cut a repo tag so article 01's snippets
compile against something. The diagram PNGs are done — exported and committed on
2026-09-02 — leaving only the WordPress upload, which is part of publishing
rather than a prerequisite for it.

---

## Editorial rules — how to make each one land

Reusable criteria, derived from what already worked in this repository.

**1 · Lead with the failure or the number, never the architecture.** Your own
notebook proves it: *"the gate caught 7 of 22 queries returning zero results"*
beats *"I built an evaluation harness"*. Your strongest existing titles —
*"Your build is green and nothing works"*, *"The catalogue nobody could find"* —
are all of this shape, and none of them was in the original plan. The tutorials
were.

**2 · One auditable before/after number per article.** Already a repo rule: a
draft may not contain a number the notebook cannot account for. This is what
separates the writing from the 95% of technical content that is opinion.

**3 · Show what the system REFUSES.** The suppressed discount with its reason.
The agent checkout denied because the mandate only covered 200 €. The idempotent
publish that saves nothing. A system that says no is memorable; a system that
says yes is a demo like any other.

**4 · Keep being honest about what broke.** *"Being wrong in public"* and
*"claiming a diagnosis before reading the logs"* are already in the notebook.
That voice is the competitive advantage, and it is precisely what a senior
architect recognises as the signal of another one.

**5 · Chain them.** Each article closes with the question the next one opens. A
series recruits readers; standalone posts do not.

**6 · The social post is not the article.** First line is the number or the
failure. Then the visual. Then three lines of context. Link last. `#ecommerce`
plus two or three specific ones (`#dotnet`, `#AIEngineering`, `#MCP`,
`#agenticcommerce`). Spanish on LinkedIn, English on X.

**7 · Treat the UCP article as a launch, not a post.** *"There is no .NET
reference implementation of UCP"* is the single most distinctive claim available
to this project. It belongs in the README, in a CV, and in a conference talk
proposal — not only in one article on one Tuesday.

---

## Phase 0 · Make the gates real

`hecha` (2026-09-02) · priority **critical** · size **M**

Everything in this phase already exists as a promise somewhere in the docs. This
turns each promise into something a pull request enforces.

- [x] `P0-1` GitHub Actions: build with `-warnaserror`, run all tests, run the architecture rules — **S** · also runs the Nx module boundaries, which were enforced by nothing
- [x] `P0-2` Elasticsearch as a service container; run `SearchEval --ci` on every PR and post the report as a comment — **S** · sticky comment, job summary as the fork-safe fallback
- [x] `P0-3` `Microsoft.AspNetCore.OpenApi`; document generated by the contract test into `docs/openapi/tendero.json`, committed — **S** · endpoints moved to `TypedResults` so the schemas are real, plus a transformer narrowing `[number, string]` to `number`
- [x] `P0-4` Contract test: committed document equals the runtime one — **S** · `UPDATE_OPENAPI=1` regenerates; verified it fails on drift
- [x] `P0-5` `openapi-typescript` generating `shared-api` types; hand-written clients kept (ADR 0010) — **S** · run via pinned `npx`, not a devDependency: its peer excludes TypeScript 6
- [x] `P0-6` EF Core Design + an initial migration reproducing today's schema exactly; `MigrateAsync` replaces `EnsureCreatedAsync` — **M** · EF tools pinned in `.config/dotnet-tools.json`; the machine's global `dotnet ef` was 10.0.2 and useless against an 11 runtime
- [x] `P0-7` Testcontainers + a database per test; four integration tests — **M** · Respawn declined, a fresh database per test is cheaper than a reset; tests skip with a reason when Docker is absent
- [x] `P0-8` `TimeProvider` injected into `Product` and `Order` — **S** · 53 call sites, which is the argument for doing it at two aggregates rather than eight
- [x] `P0-9` `SearchEval --suite` switch, search suite behind it — **S** · baseline reproduced identically afterwards
- [x] `P0-10` `TelemetrySources` constants replacing the four string literals — **S**
- [x] `P0-11` `dev-issuer`: OIDC discovery + JWKS + token minting for three seeded identities. `AddDevIssuer` throws outside Development — **M**
- [x] `P0-12` `AddJwtBearer` + CORS + the policies; `ClockSkew = 0` so an expiry test cannot pass by accident — **S**
- [x] `P0-13` `IPrincipalAccessor` in SharedKernel, with `CommercePrincipal` and `AgentId` — **S**
- [x] `P0-14` Every endpoint carries a policy or an explicit `AllowAnonymous`; two tests enforce it in both directions — **S**
- [x] `P0-15` `IdentityProviderContractTests`, abstract; the dev issuer inherits it in three lines and Keycloak will too — **M**
- [x] `P0-16` Backoffice login: identity picker, token interceptor, route guard, sign-out — **M**

**Risks.** The migration baseline is the one to get right: generate it against a
database created by the current model, and diff it, or every later migration
inherits the error. Preview EF Core may generate something odd — that is a
notebook entry either way.

The dev issuer's risk is scope creep: it exists to sign tokens for six seeded
identities, not to manage users. If it grows a registration flow or a password
reset, stop — that is the moment Keycloak was supposed to arrive. A hard guard
helps: refuse to start when `Environment` is not Development.

**Demo.** A pull request with green checks and a bot comment reading
`es NDCG@10 0.860 · en 0.720 · thresholds met`. Plus a backoffice that now asks
who you are before letting you publish.

**Evidences.** Engineering maturity; making quality gates executable rather than
documentary; contract-testing an API surface; managing a preview toolchain;
**treating the identity provider as a port so the real security path runs from
day one against a fake issuer**.

**Article.** *"The gate that never ran"* — a repo carrying a "tracked in CI"
badge with no CI, and what it costs to make eight documented guarantees real.
Opens the second half of the series. Write at close, publish 2027-01-05.
`#dotnet #CI #testing #ecommerce`

---

## Phase 1 · Variants

`hecha` (2026-09-02) · priority **high** · size **L**

- [ ] `P1-1` `VariantId` in SharedKernel; `Variant` child entity with SKU, price, axis values, tax class — **M**
- [ ] `P1-2` `Variants` and `VariantExternalReferences` as tables (ADR 0008 amendment) — **M**
- [ ] `P1-3` Implicit default variant minted at import, so every purchasable thing is a variant — **S**
- [ ] `P1-4` `OrderLine` grows `VariantId`, `Sku`, `VariantLabel` (snapshot) — **S**
- [x] `P1-5` Index is one document per `(variant, culture)`; `inStock` waits for Inventory (phase 4) — **M**
- [x] `P1-6` Query collapses on `productId`; the hit carries the winning variant's id, SKU and price — **M** · gate reproduces the baseline exactly, which was the whole argument
- [x] `P1-7` Product counts via a `cardinality` aggregation, not the hit total — **S**
- [x] `P1-8` `DefineVariants` slice + backoffice matrix generator — **M** · the whole cartesian product, with the count shown before you press
- [x] `P1-9` Storefront: price range on the card — **M** · **the variant picker is NOT done**: it needs a PDP, and there is no product detail page yet. Moved to the cart phase, where it is needed rather than decorative
- [x] `P1-10` ADR 0015 written and indexed — **S** · the connector half of the contract waits for `ExternalProduct` to carry variants, which is a Shopify-connector concern (phase 2). The default-variant guarantee is a mapper test today, because minting it is the mapper's job, not a connector's

**Risks.** The baseline must not move. Run the gate before and after; collapsed
results are product results, so the numbers should be identical — if they shift,
the collapse changed relevance and that is a bug, not an improvement. Second
risk: EF preview and a child entity table, with the backing-field friction ADR
0008 already documents. Third: deep pagination with `collapse` has rough edges —
find them at six products rather than later.

**Demo.** A card showing `24,90 – 29,90 €` with the matched colour already
selected. Then the real one: filter `navy + 38` and watch a product that has navy
in a 40 **not** appear.

**Evidences.** Choosing the indexed unit for reasons you can defend, and knowing
the false-positive facet problem exists before a customer finds it; refactoring
an aggregate without moving a measured baseline; knowing when a snapshot grows.

**Article.** *"The filter that promised a product that didn't exist"* — why
matching happens per variant and results come back per product, and what
`collapse` costs in exchange. `#search #ecommerce #Elasticsearch #DDD`

---

## Phase 2 · Localized attributes and taxonomy

`hecha` (2026-09-02) · priority **high** · size **L** · **es 0.860 → 0.943, en 0.720 → 0.937**

The highest-value phase in the floor, because it is the one that produces a
number.

- [x] `P2-1` `AttributeDefinition` aggregate, plus **aliases**: the seed sends `genero`, the label is `género`, and matching on the label would miss on the accent in silence — **M**
- [x] `P2-2` `Product.Attributes` is a typed `AttributeValue[]` — **M**
- [x] `P2-3` `IAttributeDefinitionReader` port, cached per process — **S** · invalidation lands with the editing screen; nothing can change them at runtime yet
- [x] `P2-4` The mapper resolves source keys and values against definitions; unmatched values keep the raw text rather than being dropped — **M** · the Draft-definition proposal lands with the review screen
- [x] `P2-5` `Category` aggregate: localized name, parent, materialized path. 13 categories, three levels — **M**
- [x] `P2-6` `attributesText` per culture; `categoryPathText` analysed and `categoryCode` keyword — **M** · the whole branch is indexed, not the leaf, which is what did most of the work
- [x] `P2-7` `seed/attributes.sample.json`: 14 definitions, 9 options, es+en — **S**
- [x] `P2-8` Measured: **en 0.720 → 0.811, recall 0.682 → 0.773; es unchanged** — **S** · Spanish not moving was the regression test
- [x] `P2-9` English thresholds raised to 0.80 / 0.76, same margin as before — **S**
- [x] `P2-10` Backoffice: attribute definitions screen with es/en columns and a missing-translation tag — **M** · read-only, and said so: editing needs persistence, and the table for it was created and dropped the next day for having no reader

**Risks.** The Spanish score must **not** move. If it does, the rendering changed
something it should not have — that is the regression test, and it is worth
saying so out loud in the article. Second risk: retrofitting definitions over
existing free-text attributes is the messiest data migration in the roadmap.

**Demo.** Type "navy blue" into the English label of the `NAVY_BLUE` option,
reindex, search `navy blue shoes` in English. Zero results becomes first place.

**Evidences.** Search quality as a consequence of the domain model rather than of
query tuning; measuring a change in isolation; i18n as a modelling problem.

**Article.** *"The three queries that scored zero, and what they were really
telling me"* — the flagship of the floor. One table, four numbers, one cause.
`#search #i18n #Elasticsearch #ecommerce`

---

## Phase 3 · Pricing and promotions

`no empezada` · priority **high** · size **L**

- [ ] `P3-1` `Money` grows division, percentage and a rounding + residual-allocation policy — **S**
- [ ] `P3-2` `src/Pricing` context; pure `IPriceResolver` and `IPromotionEngine` — **M**
- [ ] `P3-3` `PriceList` + entries; two lists (`retail`, `vip`); segment arrives as an input value — **M**
- [ ] `P3-4` Four effects: `PercentOffLine`, `AmountOffOrder`, `BuyXGetY`, `FreeShipping` — **M**
- [ ] `P3-5` `CombinationPolicy` as a declarative table, in the `AllowedTransitions` idiom — **M**
- [ ] `P3-6` `AppliedDiscount` carries a `RuleReason` — the suppressed-discount explanation — **S**
- [ ] `P3-7` `ITaxCalculator` keyed adapters + contract suite — **M**
- [ ] `P3-8` `PriceQuote` with expiry and input hash; ADR 0016 — **M**
- [ ] `P3-9` `POST /api/pricing/quote`, callable before Cart exists — **S**
- [ ] `P3-10` CsCheck properties: totals never negative, stackable sets order-independent, exclusives never co-apply — **M**
- [ ] `P3-11` Backoffice promotions screen with a combination column — **M**

**Risks.** Rounding. Percentage discounts plus tax plus an order-level discount
allocated across lines is where every commerce system leaks cents, and it is
worth a property test rather than an example test. Second risk: scope creep in
effect types — four, and no more.

**Demo.** A cart showing two stacked discounts and a third **suppressed**, with
the reason printed: *"no acumulable con Rebajas de verano"*.

**Evidences.** Modelling complex business rules declaratively; property-based
verification of a combinatorial space; keeping a context pure enough to test.

**Article.** *"The discount that didn't apply, and why the shop should say so"* —
combination rules as a table, and explainability as a domain concern.
`#DDD #ecommerce #dotnet`

---

## Phase 4 · Inventory

`no empezada` · priority **high** · size **M**

- [ ] `P4-1` `src/Inventory` context; `Warehouse` (two), `StockItem(Sku, WarehouseId)` — **M**
- [ ] `P4-2` `Reservation` with its own transition table — **M**
- [ ] `P4-3` `IStockLedger` (reserve/commit/release/receive), `IAllocationStrategy` keyed + contract suite — **M**
- [ ] `P4-4` Saga handlers over the existing outbox, both directions including compensation — **M**
- [ ] `P4-5` Architecture rule: `Inventory.*` must not depend on `Ordering.Domain` — **S**
- [ ] `P4-6` `StockLevelChanged` → `inStock` on the search document (ADR 0007 widening) — **S**
- [ ] `P4-7` Backoffice stock grid, editable; reservations list — **M**

**Risks.** Reservation expiry needs `TimeProvider` (P0-8) or it is untestable.
Compensation is the part that looks done and is not — test the cancel path
explicitly, not just the happy one.

**Demo.** Set MAD to 0 and BCN to 3; watch allocation pick BCN. Set both to 0;
watch the order auto-cancel and the reservation appear as `Released`.

**Evidences.** Cross-context consistency without shared entities; a saga built on
an outbox rather than a framework; compensation as a first-class path.

**Article.** *"My saga was already there and I hadn't noticed"* — the outbox as
the process manager. `#distributedsystems #DDD #ecommerce`

---

## Phase 5 · Cart, checkout, payments, returns

`no empezada` · priority **critical** · size **XL** — the gateway phase

Everything agent-native depends on this being real.

- [ ] `P5-1` `Cart` aggregate; lines as a table; guest token; expiry — **M**
- [ ] `P5-2` `Address` value object; shipping and billing on `Order` — **S**
- [ ] `P5-3` `IShippingRateProvider` keyed (`flat-rate`, `weight-bands`) + contract suite — **M**
- [ ] `P5-4` `IPaymentProvider` keyed (`fake` with failure injection, `stripe-mock`) + contract suite — **M**
- [ ] `P5-5` `PlaceOrder`: quote revalidation, hash mismatch path, idempotency key — **M**
- [ ] `P5-6` Payment webhook endpoint with signature verification and replay protection — **M**
- [ ] `P5-7` `ReturnRequest` aggregate + its transition table; window opens on `OrderDelivered` — **M**
- [ ] `P5-8` Refund via the payment port; restock via the stock ledger — **M**
- [ ] `P5-9` Storefront: cart, checkout, order confirmation, order page with a return request — **L**
- [ ] `P5-10` Backoffice: orders list, order detail, ship/deliver, returns queue — **L**
- [ ] `P5-11` Playwright: search → cart → checkout, and login → review → publish — **M**
- [ ] `P5-12` Delete the dead `product.addToCart` and `nav.orders` keys by using them — **S**

**Risks.** The largest phase by far, and the one where "lab depth" is hardest to
hold — resist gift cards, partial shipments and split payments. Second risk:
returns per line, not per order; if it starts leaking states into `Order`, stop
and reread the design.

**Demo.** The whole loop on six products: add to cart, two discounts and VAT,
three shipping options, pay with the fake provider, ship, deliver, request a
return, refund, stock comes back.

**Evidences.** Owning the commerce plane; ports with contract suites across three
adapters; a state machine that stays readable because the second one is separate.

**Article.** *"Returns are not a state of the order"* — why a five-state
per-order machine was the wrong answer, and what the `OrderDelivered` comment had
already predicted. `#DDD #ecommerce #dotnet`

---

## Phase 6 · WebMCP

`no empezada` · priority **high** · size **M** — the cheapest differentiator

- [ ] `P6-1` `libs/shared/agent`: registration mechanism, feature-detected — **S**
- [ ] `P6-2` Storefront tools: `search_products`, `apply_filter`, `add_to_cart`, `get_cart` — **M**
- [ ] `P6-3` Tools call the same Angular services the UI calls — enforced by review and an eslint boundary — **S**
- [ ] `P6-4` Descriptors in one file, so a spec change is one edit — **S**
- [ ] `P6-5` Degradation: without `navigator.modelContext` the shop is unchanged — **S**

**Risks.** The spec is under incubation at the W3C Web Machine Learning CG and
Chrome-only — label it as such in the article rather than presenting it as
settled. It will change; that is fine because it is additive and degradable.

**Demo.** In Chrome, ask the browser agent to find a navy backpack under 40 € and
add it to the cart. It happens **in whatever session you already had** — guest or
signed in — because the agent inherits it. No second principal, no token of its
own, no mandate to verify.

**Evidences.** Reading an emerging standard and integrating it without betting on
it; understanding that the trust model — not the transport — is what distinguishes
agent surfaces.

**Article.** *"Three ways to open a shop to agents, and when each one is right"* —
WebMCP vs an MCP server vs UCP, compared by trust model. Almost nobody can write
this one, and it is the piece that positions everything after it.
`#WebMCP #MCP #agenticcommerce #ecommerce`

---

## Phase 7 · Accounts, audit, and swapping the issuer

`no empezada` · priority **critical** · size **L**

Authentication already works from phase 0 against the fake issuer. This phase
adds the *domain* half — who the customer is — and then swaps the signer for a
real one, which should be a configuration change if phase 0 was done properly.

- [ ] `P7-1` `src/Accounts`: `Customer` with nullable `Subject`; guests first class — **M**
- [ ] `P7-2` `ClaimGuestAccount(cartToken, subject)` — link or merge on login — **M**
- [ ] `P7-3` Audit decorator on the dispatcher; `audit.AuditEntries` — **M**
- [ ] `P7-4` Audit screen; storefront account page with order history — **M**
- [ ] `P7-5` Verify the Aspire Keycloak integration's licence and exact version against 13.5.3 (ADR 0006 discipline). Fallback: plain container resource — **S**
- [ ] `P7-6` `AddKeycloak(...).WithRealmImport("./realms")`; realm JSON committed with the same users, roles and clients the dev issuer seeds — **M**
- [ ] `P7-7` Keycloak inherits `IdentityProviderContractTests` from phase 0 and must pass it unchanged — **S**
- [ ] `P7-8` Point `AddJwtBearer` at Keycloak. **If this needs more than configuration, phase 0 leaked** — **S**
- [ ] `P7-9` Agent `client_credentials` and Token Exchange — the two things the fake issuer could not honestly provide — **M**
- [ ] `P7-10` ADR 0017 — **S**

**Risks.** The realm file is the whole "clone and run" promise; if it drifts from
what the code expects, a fresh clone breaks in a way that is tedious to diagnose.
Export it from a running instance and commit it, do not hand-write it. Second
risk: the seeded identities must match between the dev issuer and the realm, or
every test fixture forks in two.

**Demo.** The swap itself: same login screen, same policies, same tests — one
configuration value pointing at a different issuer. Then the audit row naming who
published what, with a trace id.

**Evidences.** Treating an identity provider as a replaceable adapter with a
contract suite; cross-cutting concerns as dispatcher decorators; guest-to-account
merge as a domain operation.

**Article.** *"I faked the identity provider, not the authentication"* — why the
fake was the issuer and never the token validation, and what the swap cost.
`#dotnet #Keycloak #OIDC #architecture #ecommerce`

---

## Phase 8 · Embeddings, hybrid search, evals

`no empezada` · priority **high** · size **L**

- [ ] `P8-1` Qdrant and Ollama in the AppHost — **S**
- [ ] `P8-2` `bge-m3` via `Microsoft.Extensions.AI`; `EmbeddingModelDescriptor` — **M**
- [ ] `P8-3` Collection name carries model and dimensions; payload carries `modelId`; startup mismatch marks the layer unavailable — **M**
- [ ] `P8-4` `ProjectProductToVectors` on the existing outbox; `reindex?target=` — **M**
- [ ] `P8-5` `ISearchStrategy` keyed; RRF fusion (pure) — **M**
- [ ] `P8-6` `bge-reranker-v2-m3` behind `IReranker`, with a timeout budget — **M**
- [ ] `P8-7` Staged degradation + `search.degraded` tag and counter — **S**
- [ ] `P8-8` `HybridDegradesToLexical` test — byte-identical results with a throwing vector fake — **S**
- [ ] `P8-9` Five feature flags with removal conditions; the enum-coverage test; ADR 0023 — **S**
- [ ] `P8-10` Hybrid thresholds committed; lexical must still clear its own — **S**
- [ ] `P8-11` Backoffice search-evaluation panel, two columns with the delta — **M**

**Risks.** The vocabulary-gap queries in the golden set are the ones hybrid has
to earn; if it does not move them, publish that too. Second risk: first Ollama
run pulls gigabytes — document it in the README prerequisites as the Elasticsearch
heap already is.

**Demo.** Split screen, same query: lexical left, hybrid right, NDCG delta below.
`reading light with usb` finally returns something.

**Evidences.** Hybrid retrieval built correctly; **proven** graceful degradation;
AI quality as a CI gate; making a mixed vector space structurally impossible.

**Article.** *"Hybrid search in .NET, and the test that proves it degrades"* —
the degradation test is the part nobody writes. `#RAG #search #dotnet #AIEngineering`

---

## Phase 9 · Read-only MCP and the UCP manifest

`no empezada` · priority **critical** · size **M** — the reputational phase

- [ ] `P9-1` `src/Mcp` project; `MapMcp()` in the API process — **M**
- [ ] `P9-2` Five read tools over `IQueryDispatcher`; flat ids on the wire — **M**
- [ ] `P9-3` Architecture rule: `Mcp` implements no handler and touches no `*.Domain` — **S**
- [ ] `P9-4` Culture over MCP: tool parameter → session default → `es`; `missingCultures` as data; ADR 0013 amendment — **S**
- [ ] `P9-5` `src/Ucp` becomes a real project — **S**
- [ ] `P9-6` `IUcpCapability` + registry by assembly scanning; capabilities name their slices — **M**
- [ ] `P9-7` `/.well-known/ucp` derived from the registry; Verify snapshot — **M**
- [ ] `P9-8` `docs/mcp.md` with the Claude Desktop / VS Code config — **S**
- [ ] `P9-9` ADR 0020, ADR 0021 — **S**

**Risks.** UCP is young; pin the capability version strings and treat the schema
URIs as versioned. Do not claim conformance you have not tested.

**Demo.** Claude connected to Tendero, asked in English for a navy backpack under
40 € and what it is made of — answered from `search_products` and
`get_product_specs`. Record this one.

**Evidences.** Implementing a protocol from a specification; a manifest derived
from code rather than hand-maintained; reusing an application layer across
transports without duplicating it.

**Article.** *"A UCP manifest that cannot lie"* — derivation from a capability
registry, so deleting a slice breaks the build.
`#MCP #UCP #agenticcommerce #dotnet #ecommerce`

---

## Phase 10 · Knowledge: claims with provenance

`no empezada` · priority **high** · size **L**

- [ ] `P10-1` `src/Knowledge`; `SourceDocument` content-addressed like `ImageId` — **M**
- [ ] `P10-2` `ProductClaim` with evidence, confidence, provenance (model, revision, prompt version, run) — **M**
- [ ] `P10-3` `IClaimExtractor` via MEAI structured output — **M**
- [ ] `P10-4` `ProposeClaimsFromDescription` — no new sources, no scraping — **M**
- [ ] `P10-5` Review queue for claims, reusing the existing pattern — **M**
- [ ] `P10-6` `ClaimApproved` → outbox → Catalog `SetAttribute` and Search `specsText`. Knowledge never writes to Catalog — **M**
- [ ] `P10-7` `CompareProducts`: approved claims only; "sin dato", never a guess — **M**
- [ ] `P10-8` PDP specs table with source chips and confidence — **M**
- [ ] `P10-9` `claims` eval suite — **M**
- [ ] `P10-10` ADR 0018 — **S**

**Risks.** Confidence is easy to display and hard to calibrate — say plainly in
the article that a model's self-reported confidence is not a probability. Second
risk: source documents are user-facing text from an external origin, so treat
extracted content as data, never as instructions.

**Demo.** A specs table where every row carries a source chip; hover shows the
exact sentence. Reject one claim in the backoffice and watch it leave the PDP.

**Evidences.** Provenance and confidence modelled structurally; human-in-the-loop
where approval has a real effect; refusing to let a model write to the aggregate.

**Article.** *"The AI writes claims, not products"* — the one-sentence rule that
keeps an aggregate from rotting. `#AIEngineering #RAG #DDD #ecommerce`

---

## Phase 11 · UCP transactional, AP2 and Know Your Agent

`no empezada` · priority **critical** · size **XL** — **the headline**

- [ ] `P11-1` Cart, discount, fulfillment, checkout and orders capabilities in the manifest — **L**
- [ ] `P11-2` `AgentPrincipal` in Accounts; `CommercePrincipal(Customer, Agent?, Mandate?)` — **M**
- [ ] `P11-3` Agent tokens: `client_credentials`, and Token Exchange for acting on behalf — **M**
- [ ] `P11-4` `IMandateVerifier` keyed by format version; `fake` self-signed JWS as default — **M**
- [ ] `P11-5` `MandateScope` snapshotted onto the order, never re-resolved — **S**
- [ ] `P11-6` `IAgentPolicy` decorator; three declarative rules; denials recorded — **M**
- [ ] `P11-7` Agent activity panel over the audit table — **M**
- [ ] `P11-8` `com.elguerre.tendero.knowledge` extension capability — **S**
- [ ] `P11-9` ADR 0022 — **S**
- [ ] `P11-10` README leads with the reference-implementation claim; talk proposal drafted — **S**

**Risks.** AP2's wire format is firming through H2 2026 into early 2027 — that is
exactly why the verifier is keyed by format version. State the draft you
implemented and its date, in the code and in the article.

**Demo.** The activity panel: an agent searched four times, added two items,
attempted a 340 € checkout, **denied** — *"the mandate authorises up to 200 €"* —
with the mandate scope rendered beside it. **The denied row is the demo.**

**Evidences.** Implementing an emerging payment-authorization protocol; agent
identity distinct from customer identity; policy as a declarative, auditable
table.

**Article.** *"A UCP merchant server in .NET"* — the missing reference
implementation. **Treat as a launch**: README, LinkedIn, X, dev.to, a talk
proposal, and a note to the UCP community. `#UCP #AP2 #agenticcommerce #dotnet #ecommerce`

---

## Phase 12 · Copilot and AI observability

`no empezada` · priority **medium** · size **L**

- [ ] `P12-1` Grafana + Tempo + Prometheus in the AppHost (AGPLv3 is fine for a service we run unmodified — see the two-tier licensing policy) — **S**
- [ ] `P12-2` MEAI delegating client: OTel GenAI span + `telemetry.AiInvocations` row — **M**
- [ ] `P12-3` `src/Assist`; Microsoft Agent Framework, five read-only tools — **L**
- [ ] `P12-4` `ActionProposal` with a registered command type and payload — **M**
- [ ] `P12-5` Approval deserializes and dispatches through `ICommandDispatcher` — **M**
- [ ] `P12-6` `copilot` eval suite: assert the proposed command, no judge — **M**
- [ ] `P12-7` AI observability panel: cost per conversation, p50/p95, tokens per model, degradation counter — **M**
- [ ] `P12-8` ADR 0019 — **S**

**Risks.** Prompt injection through catalogue and claim text — the copilot reads
data written by importers and models. Constrain it to typed read tools over
curated tables, never raw traces, and never free SQL.

**Demo.** *"12 products have no English name — shall I translate them?"* → a table
of proposals → Approve → twelve commands dispatch → the audit log shows who
approved what. Plus the cost line: *"this conversation: 3 calls, 1,842 tokens,
€0.0021"*.

**Evidences.** Drawing the AI/deterministic boundary; making an agent's action
vocabulary a closed set; treating cost and latency as product features.

**Article.** *"My copilot doesn't write text, it writes commands"* — why the
answer ends in a button. `#AIEngineering #agents #dotnet #ecommerce`

---

## Phase 13 · Multimodal and the kit solver

`no empezada` · priority **medium** · size **L**

- [ ] `P13-1` Tokenizer-fidelity fixture test **before** committing to in-process ONNX — **S**
- [ ] `P13-2` SigLIP 2 via ONNX Runtime; `IImageEmbeddingGenerator` — **L**
- [ ] `P13-3` Image collection keyed on `ImageId`; dedup for free — **M**
- [ ] `P13-4` `POST /api/search/by-image`; then text→image — **M**
- [ ] `P13-5` `IKitSolver`, pure: branch-and-bound with a solver trace — **M**
- [ ] `P13-6` CsCheck properties: budget never exceeded, order-stable, monotone in candidates — **M**
- [ ] `P13-7` Explanation generated from the trace; a model may phrase, never add facts — **M**
- [ ] `P13-8` Kit form first; natural-language parsing second — **M**

**Risks.** Model weights need the same per-version licence check as NuGet
packages — extend the policy before adding the first one. SigLIP and SigLIP 2
spaces are not interchangeable; the collection-naming rule from P8-3 already
prevents mixing them.

**Demo.** Drop a photo of a pan into the search box and get the pan. Then a kit:
*"Kitchen kit · €118.40 of €120"* with *"I chose the 24 cm pan over the 28 cm
because €11.50 remained and the 28 costs €14.90."*

**Evidences.** In-process model inference in .NET without a Python sidecar; a
deterministic solver behind a conversational surface; explanations derived from a
trace rather than generated.

**Article.** *"A recommendation that shows its arithmetic"* — the model parses and
phrases, the solver decides. `#AIEngineering #ecommerce #dotnet`

---

## Standing backlog — deferred, with the number

Not scheduled. Each is recorded so the deferral is a decision rather than an
oversight, and each is a paragraph in some future article.

| Item | Measured today | Enters when |
|---|---|---|
| Outbox coalescing | 47 rows for 6 products (~8 events each); ~1.2M for the full 147k ABO set | A real import, or the outbox lag metric turns bad |
| Bulk indexing | one HTTP call per product per culture | Same |
| Reindex as a background job | fine for 6 products; the wrong shape for 147k | Same |
| Image derivatives | six 640px images — nothing to measure | A deployment target exists |
| Orphan index documents | 6 hard-deleted rows left 12 documents | Something other than `psql` produces the case |
| `SKIP LOCKED`, exponential backoff | one replica | A second replica |
| Full ABO import (147k) | not attempted | It is the trigger for the four rows above |
| Shopify connector | one connector today, and the port is proven | A second source is genuinely wanted |
| Multi-currency | one currency per order, enforced | A second market |
| Spec Kit experiment | ADR 0009 reserves it for the UCP work | Phase 11 |
| `.claude/` skills and plugin | none exist; article 14 is blocked on it | After enough repetition to have opinions |
