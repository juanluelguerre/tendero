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

- **Current phase:** **the blog caught up with the code.** The four diagrams in article 00 were a photograph of 2026-09-02 and had gone quietly wrong: two bounded contexts where there are five, UCP labelled phase 3 when it is 9 and 11, Qdrant labelled phase 2 when it is 8, a shipped agent surface (WebMCP) missing entirely, and **Grafana drawn as if it ran when the AppHost does not declare it** — the exact failure the article condemns two paragraphs above it. All four regenerated from their `.html` sources through `.svg` to 2000px `.png`, the article's own table corrected (6 endpoints → 33 routes, 117 tests → 503 + 37 + 20, 13 ADRs → 21) and the prose rewritten in plainer English. Articles 16 and 17 went from broken links out of `blog/index.md` to finished drafts in the same day — the two pieces the queue had been holding that were blocked on nothing — and outline 09 stopped carrying the pre-100-product baseline it would have published as a "before". Before that: a review pass for SOLID/DRY across back and front, **merged** to `develop` as PR [#2](https://github.com/juanluelguerre/tendero/pull/2) — 503 backend tests, 37 frontend and 20 browser specs green (one honest skip), every `.md` audited against the code. Since then, an evening of environment failures on Linux under Rider, none of them in the code and all three in the notebook: two dotnet roots, nvm's PATH, and a failed `docker pull` that deletes the tag it cannot replace. Before that: the storefront's look, off the board's backlog and ahead of phase 8 — **the home page is a shop now**: departments off the taxonomy phase 2 built, offers off the promotion engine, novedades off `available_from`, and a category page that browses. 495 tests, format gate clean. Merged to `develop` as PR [#1](https://github.com/juanluelguerre/tendero/pull/1) — opened as a pull request, fast-forwarded on the author's say-so, and GitHub recorded it as merged either way.
- **Next task:** the photographs, as they arrive (`seed/IMAGES-TODO.md` — the seed already points at `images/<item_id>.webp` for all hundred, so a photo is a file and nothing else); then the returns promise on the PDP, which `initial-plan.md` §2 already carries the number for. `P7-9`'s Token Exchange still waits for phase 11.
- **Next publication:** article 00 on **2026-09-15** — the four PNGs are regenerated and current; what remains is uploading them to the WordPress media library and swapping the four relative paths

**Decisions taken 2026-09-06** — 1 · **a diagram is a claim and it goes stale like any other**, so the four in article 00 are regenerated from source rather than patched, and the `.html` → `.svg` → `.png` chain is reproducible in one command · 2 · **Grafana came out of the stack diagram** because the AppHost does not declare it: the article's own "nothing is declared until code reads it" rule, applied to the article · 3 · `1 hit · score 8.91` came out of the flow diagram — that score was measured over six documents and the corpus is a hundred; the diagram says `the only Active one` now, which needs no measurement and makes the point better · 4 · **article 00 moves to plainer English (B2)**, because the English version exists for reach and the reader it is written for is not a native speaker · 5 · `blog/index.md` credited article 17 with `416 → 434` tests; fourteen of those are article 16's, and the notebook says so.

**Decisions taken 2026-09-05** — 1 · **the transition table is a type**: `TransitionTable<TStatus>` in the SharedKernel, and `Order`, `Cart`, `ReturnRequest` and `Reservation` declare their edges on it instead of each carrying its own guard and its own wording for the same refusal · 2 · **strongly-typed ids are converted once per type** through EF's pre-convention configuration, not fourteen times per property; the model snapshot did not move and the migration baseline test agreed · 3 · **a test walks every handler the API references and resolves it from the API's container**, because five composition failures in this notebook were all found by a person · 4 · **the screens are held to the agent tools' rule**: no `HttpClient` under `features/**`, so the order page and checkout go through an `OrderService` a tool can share · 5 · a page whose inputs are signals is an `rxResource` (the PDP) or holds its in-flight subscription and cancels it; a subscribe inside an effect lets the LAST answer win rather than the last one asked for · 6 · the home page and the stock grid reload on a language switch, as the review queue already did.

**Decisions taken 2026-09-04** — 1 · **the API declares its issuer** at `GET /api/auth/config`; a build-time flag in each app is two places that can disagree, and they did · 2 · Keycloak is reached over Aspire's own development certificate, which the OS already trusts, so no browser warning stands between a fresh clone and a token · 3 · **a cancelled order carries why it was cancelled**, as a code the interface translates plus a detail carrying the SKU and the numbers — the reason used to exist only inside a processed outbox message · 4 · every fixed port is deliberately off the default (55432, 59200, 58443), because 5432 and 9200 are somebody's work services · 5 · **Keycloak, pgweb and elasticvue are always DECLARED and started on demand** (`WithExplicitStart`), because a resource created only behind an environment variable is invisible to anybody launching from Visual Studio — the flag now decides whether the API TRUSTS Keycloak, not whether it exists · 6 · Elasticsearch's CORS origin names elasticvue instead of `*`: a starred origin on a node bound to 127.0.0.1 lets any page the browser visits delete these indexes, and naming it is only possible because the port is fixed · 7 · **sign-in is a redirect** (Authorization Code + PKCE) for BOTH issuers: the password grant put credentials in the application and made the swap invisible, and the development issuer grew `/connect/authorize` rather than stay the easier half · 8 · `angular-oauth2-oidc` 22.0.2 added after the ADR 0006 check — MIT, peers `>=22.0.0`, one transitive dependency already present · 9 · **every container carries the Compose project label** so Docker Desktop collapses them under one `tendero` group instead of listing eight loose containers among other projects' — the label is applied over the model rather than at each call site, because three of them are created by integrations.

**Decisions taken 2026-09-04 (evening)** — 1 · **the catalogue is 100 products**, not six: six was below the floor at which a shop can be judged, and it made the search gate measure ranking over a corpus where everything retrieved was annotated · 2 · **the NDCG baseline was re-derived rather than defended** — es 0.943 → 0.819, en 0.937 → 0.766 — because NDCG@10 over six documents and over a hundred are different measurements, and the recall drop (0.909 → 0.642) is the vocabulary gap becoming measurable instead of being one query per culture · 3 · the review queue shows what the import DID, and the awning stripe finally does the job `design/DESIGN.md` assigned it.

**Decisions taken 2026-09-04 (night)** — 1 · **the reindex endpoint had never worked**, and could not have: the reader held a Postgres reader open for the whole walk and Npgsql has no MARS, so phase 4's `IAvailabilityReader` — asked once per product, on the same connection — made it a guaranteed 500 with every test green. It reads in chunks now, and the regression test drives the real port through a real Postgres · 2 · **an index that already exists is never re-mapped**, so `categoryCodes` was typed `text` dynamically and a term filter matched nothing while reporting nothing; `reindex?recreate=true` rebuilds the mapping, and create and recreate now share one method so they cannot drift. Same finding as ADR 0012's `EnsureCreatedAsync`, one projection to the left · 3 · **the department pictures are illustrations, not photographs** — a stock photo of somebody else's shop is a promise this catalogue cannot keep — and the art sits BESIDE the words everywhere, so a real photograph can replace a drawing without contrast being re-solved · 4 · a department showing 24 of 55 products is a shop with 31 unreachable things; `Ver más` appends a page rather than an infinite scroll that eats the footer.

**Standing chore — cleared 2026-09-04.** The eleven commits that had piled up locally are on `origin/develop`, `0bfb443`'s GitHub Actions bump among them, so CI has finally seen every one of them. The push went through with the token already configured; whatever the `workflow` scope was refusing earlier, it is not refusing now — which is worth a line in the notebook rather than a claim here.

**Decisions taken 2026-09-02** — 1 · an ADR generalises the context principle rather than fixing a count · 2 · nothing is anonymous; the identity provider is a port whose first adapter is a development issuer, Keycloak later · 3 · the variant is the indexed unit and the product the returned one, via `collapse` · 4 · licensing splits into two tiers, so the Grafana stack is back in · 5 · UCP is split, read capabilities in phase 9 and the transactional half in phase 11.

**Decisions taken 2026-09-03 (evening)** — 7 · WebMCP offers `get_product` and **not** `apply_filter`: a filter tool over a shop with no facets is speculative, and facets land in phase 8 · 8 · the "same services the UI calls" rule is an **eslint boundary**, because review cannot enforce it · 9 · the browser's model context is injected rather than read from `navigator`, so the degradation is testable where it actually happens.

**Decisions taken 2026-09-03** — 1 · the outbox is the process manager, orchestrated from `Ordering` (ADR 0024) · 2 · checkout authorises the payment before it places the order, so a decline costs nothing (ADR 0025) · 3 · cart lines are jsonb, not a table — mutating one through the aggregate is not querying it · 4 · returns are their own aggregate, because they are per line · 5 · Playwright stays the thinnest layer, and no self-healing agent touches a suite whose point is what the system refuses · 6 · **the URL carries a public code and the slug is decoration** (ADR 0026), because a slug cannot be unique inside jsonb and does not survive a rename. The Shopify alternative — a slug table with history and `-2` suffixes — was built halfway and rejected for managing both problems rather than removing them.

---

## How this is ordered

The sequence optimises for **what it evidences**, not for what is cheap. Nothing
is cut for being laborious. What *is* constrained is data volume: a hundred
products, two warehouses, three roles. Scale work is deferred with its measured number
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

You start with an unusual advantage: **nine finished drafts, two writable outlines and one half-written**.
That is roughly four and a half months of publishing without writing a new line,
during which phases 0–5 refill the queue.

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
| 2027-01-05 | 15 · The bot you did need, and the two you didn't | ready — pairs with 07, publish it right after |
| 2027-01-19 | 16 · The URL that guessed which product you meant | ready |
| 2027-02-02 | 17 · The slice that could not run | ready — publish right after 16 |
| 2027-02-16 | **NEW** · from phase 0 | write when P0 closes |
| 2027-03-02 | **NEW** · from phase 2 | write when P2 closes |
| 2027-03-16 | **NEW** · from phase 3 | write when P3 closes |
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
Opens the second half of the series. Write at close, publish 2027-01-19.
`#dotnet #CI #testing #ecommerce`

---

## Phase 1 · Variants

`hecha` (2026-09-02) · priority **high** · size **L**

- [x] `P1-1` `VariantId` in SharedKernel; `Variant` child entity with SKU, price, axis values, tax class — **M**
- [x] `P1-2` `Variants` as a table (ADR 0008 amendment) — **M** · `VariantExternalReferences` is NOT built: the seed carries no per-variant source ids, and the table waits for the connector that does (the Shopify concern `P1-10` names)
- [x] `P1-3` Implicit default variant minted at import, so every purchasable thing is a variant — **S**
- [x] `P1-4` `OrderLine` grows `VariantId`, `Sku`, `VariantLabel` (snapshot) — **S**
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

`hecha` (2026-09-02) · priority **high** · size **L** · **73 pricing tests, 239 in total**

- [x] `P3-1` `Money` grows subtraction, division, percentage, comparison, a named rounding policy and largest-remainder allocation — **S**
- [x] `P3-2` `src/Pricing` context; pure `IPriceResolver` and `IPromotionEngine` — **M** · an architecture rule computes, by reflection, that Pricing references SharedKernel and nothing else
- [x] `P3-3` `PriceList` + entries; two lists (`retail`, `vip`) — **M** · **keyed on SKU, not `VariantId`**: the internal id is a GUID v7 minted per import, so a tariff keyed on it would expire on every reimport — the same call the golden set already made
- [x] `P3-4` Four effects: `PercentOffLine`, `AmountOffOrder`, `BuyXGetY`, `FreeShipping` — **M** · four branches in one place; the effect is data and the arithmetic is the engine's
- [x] `P3-5` `CombinationPolicy` as a declarative table, in the `AllowedTransitions` idiom — **M** · three rows, two booleans, and a test that walks the enum in full
- [x] `P3-6` `AppliedDiscount` carries a `RuleReason` — **S** · suppressed promotions come back too, with a code a test asserts and a text in es+en
- [x] `P3-7` `ITaxCalculator` keyed adapters + contract suite — **M** · `flat-vat` and `zero`, seven shared assertions each
- [x] `P3-8` `PriceQuote` with expiry and input hash; ADR 0016 — **M** · the hash covers the tariffs and promotions too, which is the half that gets forgotten
- [x] `P3-9` `POST /api/pricing/quote`, callable before Cart exists — **S** · `AllowAnonymous`, but the **segment comes from the principal and never from the body**: a guest asking for `vip` is quoted `retail`
- [x] `P3-10` CsCheck properties — **M** · seven of them at 2,000 carts each, and **one found a real bug**: the engine matched lines by SKU, so a cart with the same SKU twice threw while allocating an order discount. Matches by position now
- [x] `P3-11` Backoffice promotions screen with a combination column — **M** · read-only, and says so: promotions come from a committed file, and the table for editing them waits for an editor

**Risks, and what actually happened.** Rounding was the predicted one, and it
behaved: `Money.Allocate` plus a single named policy applied once at the end
held across 5,000 generated allocations. The risk that *bit* was not on the list
— matching lines by SKU instead of by position, found by a property test in a
cart the API cannot currently produce. Scope creep in effect types did not
happen: four, and no more.

**Demo.** `POST /api/pricing/quote` with two shirts and a pan: two discounts
applied and a third **suppressed**, with the reason printed —
*"No acumulable con Rebajas de verano."* Then the same cart as a guest and as a
signed-in VIP, and watch the unit price change without the request changing.

**Evidences.** Modelling complex business rules declaratively; property-based
verification of a combinatorial space; keeping a context pure enough to test.

**Article.** *"The discount that didn't apply, and why the shop should say so"* —
combination rules as a table, and explainability as a domain concern.
`#DDD #ecommerce #dotnet`

---

## Phase 4 · Inventory

`hecha` (2026-09-03) · priority **high** · size **M** · **303 tests, es 0.943 / en 0.937 unmoved**

- [x] `P4-1` `src/Inventory` context; `Warehouse` (two), `StockItem(Sku, WarehouseId)` — **M** · keyed on **SKU, not `VariantId`**, which is what keeps Inventory from referencing Catalog at all
- [x] `P4-2` `Reservation` with its own transition table — **M** · four states, and a refusal is born `Released` carrying its reason
- [x] `P4-3` `IStockLedger` (reserve/commit/release/receive/**count**), `IAllocationStrategy` keyed + contract suite — **M** · `count` was not on the list and had to be: receiving nothing is a delivery that did not happen, and only a stocktake can say zero
- [x] `P4-4` Saga handlers over the existing outbox, both directions including compensation — **M** · **orchestrated from Ordering**, because the roadmap's own architecture rule contradicted its arrows (ADR 0024). Four integration tests drive it through a real Postgres and the real outbox
- [x] `P4-5` Architecture rule: `Inventory.*` must not depend on `Ordering.Domain` — **S** · two rules, and both had to be checked against a real coupling: the compiler omits an unused ProjectReference from the manifest, so a rule written against references alone passes vacuously
- [x] `P4-6` `StockLevelChanged` → `inStock` on the search document (ADR 0007 widening) — **S** · and exposed on the hit, because a field nobody reads is the half-built seam this repo already complains about
- [x] `P4-7` Backoffice stock grid, editable; reservations list — **M** · the first screen in the backoffice that **writes**

**Risks, and what actually happened.** Compensation was the predicted one and it
behaved — but only because the test drains the outbox **twice**: the cancellation
the saga itself causes is a new message, and a test that drained once would have
looked green while the release never ran.

Three failures were not on the list, and all three are the same failure.
**Composition is invisible to every gate this repository owns.** The saga shipped
with the worker scanning only `Search`'s assembly, so orders would have been
placed, messages drained, and no stock ever held — the outbox marks a message
processed whether or not anybody handled it. The NDCG gate composes its own
container and had been broken since the indexer grew an `IAvailabilityReader`;
nobody noticed because nothing runs it locally. And the seeder filtered its zero
rows out, so the out-of-stock product the demo exists to show had no row at all.

The fourth was real and only phase 4 could have caused it: `delete_by_query`
aborts with a 409 when a document's version moved under its snapshot, and two
projections now rebuild the same product. **Ten dead-lettered messages, and
`inStock` stuck false on a shop that had stock.** The projection writes first and
sweeps after now, which also closes the window where a published product was
briefly absent from the index.

**Demo.** Type 0 into the pans row in the backoffice. The count goes through the
ledger, raises `StockLevelChanged`, drains through the outbox, reindexes the
product — and the storefront card comes back desaturated with *Sold out* beside a
coffee maker that is not. Then the refusal: an order for more than exists cancels
itself, and the reservation is there in `Released` saying
*"PANS: 2 asked for, 1 available."*

**Evidences.** Cross-context consistency without shared entities; a saga built on
an outbox rather than a framework; compensation as a first-class path.

**Article.** *"My saga was already there and I hadn't noticed"* — the outbox as
the process manager, and the three composition failures that no build, no test
and no architecture rule could see. Write at close, publish per calendar.
`#distributedsystems #DDD #ecommerce`

---

## Phase 5 · Cart, checkout, payments, returns

`hecha` (2026-09-03) · priority **critical** · size **XL** — the gateway phase · **434 tests, es 0.943 / en 0.937 unmoved**

Everything agent-native depends on this being real.

- [x] `P5-1` `Cart` aggregate; lines as a table; guest token; expiry — **M** · lines are **jsonb, not a table**: the roadmap's reason was "UCP mutates single lines", and mutating one through the aggregate is not querying it. ADR 0008's actual criterion sends them to a column
- [x] `P5-2` `Address` value object; shipping and billing on `Order` — **S** · in the **SharedKernel**, because three contexts need the shape and none owns it
- [x] `P5-3` `IShippingRateProvider` keyed (`flat-rate`, `zone-rate`) + contract suite — **M** · **not `weight-bands`**: the catalogue carries no weight, and adding one changes `attributesText`, which moves a measured search baseline for a reason unrelated to search. The two adapters differ in the input they READ instead, which tests the port better
- [x] `P5-4` `IPaymentProvider` keyed (`fake` with failure injection) + contract suite — **M** · the suite ADR 0003 described in the present tense and `docs/testing.md` named by filename. **Four** operations, not three: `VoidAsync` exists because an order that authorises and cannot be filled has to give the hold back. `stripe-mock` needs a NuGet package and a container — a dependency decision, not a coding one
- [x] `P5-5` `PlaceOrder`: quote revalidation, hash mismatch path, idempotency key — **M** · revalidation re-runs the **same engine** through one confined adapter (ADR 0025); a checkout with its own arithmetic would compare two hashes from two systems and call it agreement
- [x] `P5-6` Payment webhook endpoint with signature verification and replay protection — **M** · HMAC over `<timestamp>.<raw body>`, fixed-time compare, five-minute tolerance. Three refusals and three verdicts: forging, replaying, and none of our business
- [x] `P5-7` `ReturnRequest` aggregate + its transition table; window opens on `OrderDelivered` — **M** · six states, and the edge that is easy to leave out is `Received → Rejected`: the parcel arrived and the goods are not what the reason claimed
- [x] `P5-8` Refund via the payment port; restock via the stock ledger — **M** · **receiving** restocks, not approving, and damaged goods never do. The refund carries the returned lines' share of the order's discount and tax
- [x] `P5-9` Storefront: cart, checkout, order confirmation, order page with a return request — **L** · the cart shows **suppressed** promotions with their reason, which is the phase-3 engine finally visible
- [x] `P5-10` Backoffice: orders list, ship/deliver, returns queue — **L** · the orders table shows **authorised** and **captured** apart, because a shipped order that was never captured is the row worth finding
- [x] `P5-11` Playwright — **M** · **6 backoffice specs + 8 storefront**, 7.8s, against the real stack. The storefront half waited on the PDP because the PDP changed the flow it would test. The pyramid's thinnest layer on purpose: the loop is already covered against a real Postgres, so these assert only what a browser can — the guard, the interceptor, and a table that never keeps its own copy of `AllowedTransitions`. Verified by mutation: a heading downgraded to a `<p>` turns the tab spec red
- [x] `P5-12` Delete the dead `product.addToCart` and `nav.orders` keys by using them — **S** · both used. `nav.agents` and `nav.search` stay dead on purpose: they belong to phases 11 and 8, and they are the gap analysis written in i18n
- [x] `P5-13` **The locale and the query go into the URL** — **M** · `?q=` and now `/es/…`. A path with no language gets one rather than a 404, switching NAVIGATES instead of flipping a signal, and the PDP carries canonical + `hreflang` listing itself among its alternates. One route table mounted once per culture, so `/es/cart` and `/en/cart` cannot drift
- [x] `P5-14` `GET /api/catalog/products/{code}` — **M** · not on the original list, and it should have been: the PDP was three tasks' blocker and had no query. Reads the catalogue and not the index, because the page needs structured attributes and out-of-stock variants and has to survive Elasticsearch being down. Catalog became the third context to reference Inventory's ports, so the picker renders **once**. Keyed on a **public code**, not the slug: a slug cannot carry a unique constraint inside jsonb and does not survive a rename, so looking a product up by one meant guessing between two and 404ing every link on a typo fix (ADR 0026). The first version WAS by slug, and replacing it deleted three runtime-only failures along with the hand-written SQL that caused them

- [x] `P5-15` **The product detail page** — **L** · not on the original list, and it is what `P1-9`, `P5-13` and `P5-11` had all been waiting on. The picker shows every option the catalogue defines with **three** states — chosen, sold out (selectable), not sold (disabled and still rendered) — because an option that vanishes tells a shopper their size is not made. Building it found two older defects: `DefineVariants` was **unreachable for the entire shipped catalogue** (the axis guard counted variants rather than coordinated ones, and importing mints an implicit `-DEFAULT`), and an axis with no definition rendered a control with nothing to press

**What building it found.** Two defects older than this phase, both invisible
until a screen needed them. `DefineVariants` had tests, shipped in phase 1, and
could not run on a single real product — every imported product carries an
implicit `-DEFAULT` variant, and the guard refused to declare axes while any
variant existed. And the seed declares `COLOR` but no `SIZE`, so the axis came
back with an empty option list: a dead control above six variants that plainly
had sizes.

**Old text, kept because it was the plan.** The product detail page had a
**query** and not yet a screen. Phase 1 deferred the variant picker to "the cart phase, where it is
needed rather than decorative", and the cart phase went straight from the search
card to the basket — which works at one variant per product and would not at
eight. `P5-14` closed the half that was three tasks' blocker; the Angular route
is what `P5-13`'s locale segment, phase 1's picker (`P1-9`) and the storefront
spec (`P5-11`) now wait on, and it is the next session's first task.

**Risks, and what actually happened.** The scope creep the phase was warned about
did not happen: no gift cards, no partial shipments, no split payments. Returns
stayed out of `Order` and are per line.

What bit instead was **composition, for the fifth time**. Carter only scans
assemblies that reference it **directly**; `Ordering` inherited it transitively,
compiled cleanly, and cart, checkout, returns and the webhook were absent from
the running API and from the OpenAPI document with every test green. And
`IPrincipalAccessor` documented the worker case while nobody had registered
`SystemPrincipalAccessor` — caught this time by a test, which is the first time
one of these was found before a person found it.

The migration baseline test earned its keep too: EF generated `DEFAULT '{}'` on
the new jsonb columns, the model declares none, and the schema comparison went
red on the drift.

**Demo.** The whole loop on six products, and the refusals are the point. Add two
pairs of shoes: the cart shows one discount applied and **four suppressed, each
with its reason**. Pay with the card that declines — no order, no reservation,
the basket untouched. Pay again with the good one: stock is held and the order
confirms itself through the outbox. Ship it and the money is captured and the
shelf goes 4 → 2. Deliver it, send one back, and receiving puts the shelf at 3
before a euro moves.

**Evidences.** Owning the commerce plane; ports with contract suites across three
adapters; a state machine that stays readable because the second one is separate.

**Article.** *"Returns are not a state of the order"* — why a five-state
per-order machine was the wrong answer, and what the `OrderDelivered` comment had
already predicted. `#DDD #ecommerce #dotnet`

---

## Phase 6 · WebMCP

`hecha` (2026-09-03) · priority **high** · size **M** — the cheapest differentiator · **6 agent specs, 28 frontend tests**

- [x] `P6-1` `libs/shared/agent`: registration mechanism, feature-detected — **S** · the browser's context is an **injection token** defaulting to null, because reaching for `navigator` inside the service makes the degradation untestable in the one environment that matters — jsdom has no `modelContext`, so every test would take the "unsupported" branch and the registration path would never run
- [x] `P6-2` Storefront tools — **M** · `search_products`, **`get_product`**, `add_to_cart`, `get_cart`. **Not `apply_filter`**: a filter tool over a shop with no facets is a tool over something that does not exist, and facets are phase 8's aggregations. `get_product` replaced it because `add_to_cart` takes a SKU and only that says which SKUs exist
- [x] `P6-3` Tools call the same Angular services the UI calls — **S** · an **eslint boundary**, not review: a tool file may not import `HttpClient`. Verified by mutation in both directions
- [x] `P6-4` Descriptors in one file, so a spec change is one edit — **S** · and a spec asserts the exact list, so a fifth tool is a deliberate act rather than a drift
- [x] `P6-5` Degradation: without `navigator.modelContext` the shop is unchanged — **S** · tested in jsdom (the real absence, not a simulated one) **and** in a browser: no errors, no visual difference, nothing registered

**What the demo actually shows.** An agent inside the shopper's tab searching,
being **refused** a one-character query and an unknown code, adding two variants
to *their* cart, and reading it back with the suppressed discounts and their
reasons — *"10% en ropa: not applied — No acumulable con Rebajas de verano."*
The phase-3 promotion engine reaching a model, refusals intact. **Nothing in that
flow authenticates**, which is the entire point.

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

`hecha` (2026-09-04, Token Exchange excepted) · priority **critical** · size **L** · **the swap was two environment variables**

Authentication already works from phase 0 against the fake issuer. This phase
adds the *domain* half — who the customer is — and then swaps the signer for a
real one, which should be a configuration change if phase 0 was done properly.

- [x] `P7-1` `src/Accounts`: `Customer` with nullable `Subject`; guests first class — **M** · a guest is a `Customer` with no subject, **not the absence of one**. `Segment` lives here rather than in `Pricing`, because a segment is a fact about a PERSON and Pricing is a pure function of values. No roles: they live in the token and map to policies, and a domain storing one would have two sources of truth for authorisation
- [x] `P7-2` `ClaimGuestAccount(cartToken, subject)` — link or merge on login — **M** · built as **two** commands in two contexts, not one: `POST /api/accounts/me` (Accounts) establishes the customer, `POST /api/cart/claim` (Ordering) attaches the basket. One operation would mean a context reaching into the other, and **Ordering will need Accounts for order history**, so the dependency this way would close a cycle — an architecture rule now says so. Three outcomes: registered, linked (the guest keeps its id, so yesterday's order stays theirs) or recognised (the guest is superseded, never deleted). The subject comes from the token and never from the body; a merge refuses anything that is not a guest, which is the security boundary rather than tidiness
- [x] `P7-3` Audit decorator on the dispatcher; `audit.AuditEntries` — **M** · writes on its OWN connection, outside the command's transaction (ADR 0027): a row inside it disappears exactly when the command fails, so the log would hold every success and no refusal. Three outcomes and not two — `Denied` is the system WORKING, and it is the row phase 11's panel opens on. Validation failures and queries leave no row. Credentials are redacted, which is not hypothetical: the guest claim and checkout both carry a cart token
- [x] `P7-4` Audit screen; storefront account page with order history — **M** · the audit screen **opens on the refusals**, because a log whose default view is a thousand successful publishes is a changelog — the same reason denials have their own partial index. Agent rows render in clay (`design/DESIGN.md`), so scanning shows at a glance what an agent did rather than a person. The storefront got auth wiring it never had: `GET /api/orders/mine` is a **separate slice** from the shopkeeper's list, because a nullable customer filter sitting one refactor away from a shopper's screen is a leak the type system would not mention
- [x] `P7-5` Verify the Aspire Keycloak integration's licence and exact version against 13.5.3 (ADR 0006 discipline) — **S** · **passes both halves** where `Aspire.Hosting.Elasticsearch` failed: MIT declared in the nuspec, and its only Aspire dependency is exactly the `Aspire.Hosting 13.5.3` the AppHost resolves. One trap worth knowing: **all 46 published versions are previews**, so `dotnet package search` returns "No results found", which reads exactly like a package that does not exist
- [x] `P7-6` `AddKeycloak(...).WithRealmImport(...)`; realm JSON committed — **M** · two failures only running it could find, and both looked identical: a token that validated perfectly while every policy denied. Keycloak treats `.` in `claim.name` as a **nesting separator**, so `ClaimTypes.Role`'s URI became an object rooted at `http://schemas`; and declaring `clientScopes` at realm level **replaces** Keycloak's built-in set, so `profile`, `email` and `roles` stopped existing
- [x] `P7-7` Keycloak inherits `IdentityProviderContractTests` — **S** · **and it did NOT pass unchanged, which is the finding.** The suite encoded its only adapter three times: a relative path, the test server's channel, and a JWKS at the dev issuer's location. It follows the `jwks_uri` discovery advertises now, which is truer and is what `JwtBearer` actually does. *A contract suite with one implementation has not been tested as a contract.* 6/6 green against a live realm
- [x] `P7-8` Point `AddJwtBearer` at Keycloak — **S** · **two environment variables.** `Program.cs` defaults the authority with `??=`, so setting it IS the swap: no code path, no second scheme, no branch. Verified live — shopkeeper 200, agent 403, no token 401, public catalogue 200
- [~] `P7-9` Agent `client_credentials` and Token Exchange — **M** · **`client_credentials` done**: `claude-desktop` authenticates as itself, holds the `agent` role and carries a hardcoded `agent_id` claim, which is exactly what `CommercePrincipal` reads to tell an agent from a person. **Token Exchange is not**, and it belongs beside the mandates in phase 11: acting on behalf of somebody is only meaningful when there is something authorising the scope
- [x] `P7-10` ADR 0017 — **S** · the decision AND the evidence, which arrived a phase apart

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
| Orphan index documents | 6 hard-deleted rows left 12 documents; running SearchEval against the stack's own Elasticsearch left 6 more, because the gate mints fresh product ids per run and the sweep filters by `productId` | Something other than `psql` produces the case. Meanwhile: **do not point the gate at a running stack's engine** |
| `SKIP LOCKED`, exponential backoff | one replica | A second replica |
| Full ABO import (147k) | not attempted | It is the trigger for the four rows above |
| Shopify connector | one connector today, and the port is proven | A second source is genuinely wanted |
| Multi-currency | one currency per order, enforced | A second market |
| Promotions and tariffs in Postgres | 2 lists, 7 promotions, both in committed files | The backoffice can edit one. Building the table first is what phase 2 already did and undid |
| Reservation expiry sweep | `Reservation.Lifetime` is 15 min and `HasExpiredAt` exists; nothing sweeps. Carts exist now and expire in 7 days, and `ix_carts_status_expires_at` is already the index the sweep would read | A scheduled worker. Checkout authorises before it places (ADR 0025), so a declined card leaves nothing behind — which is what took the urgency out of this |
| `stripe-mock` as a second payment adapter | one adapter, one contract suite; the same shape `SeedCatalogConnector` and `dev-issuer` ship in | It needs `Stripe.net` (or hand-rolled form-encoded HTTP) and a container. A dependency decision, and the port is proven without it |
| Cart lines as a table | jsonb, read only with the cart that owns them | Something queries a line on its own. UCP mutating one goes through the aggregate, so it is not that |
| Locale in the URL (`/es/…`) | one route serves two languages; the query is in the URL now, the language is still in a signal and in `localStorage` | The PDP route, which is where the per-culture slugs and `hreflang` both land (`P5-13`). The query already answers with every culture's slug |
| A slug table with uniqueness and redirects | **settled, not deferred** — ADR 0026 makes the slug decorative and the URL carries a code, so there is nothing to make unique and no history to keep | Never, unless the code leaves the URL |
| Spec Kit experiment | ADR 0009 reserves it for the UCP work | Phase 11 |
| `.claude/` skills and plugin | none exist; article 14 is blocked on it | After enough repetition to have opinions |
| **A storefront that looks like a shop** | the home page IS the search box; no landing, no browsable taxonomy, no editorial imagery, no returns or delivery promise on the PDP | Deliberately after the floor. See below — it is the largest single item here and the only one that is not deferred for scale |

### A real account, and the menu it earns

Recorded 2026-09-04, unscheduled. Today the account page is one screen: who you
are, sign out, and your orders. A shop people come back to has more, and the
list is worth writing down because each item needs a different amount of work
and two of them are not ours to hold.

| section | what it needs | where it lands |
|---|---|---|
| **Mis pedidos** | nothing — it ships | done |
| **Direcciones** | `Address` exists in the SharedKernel and is frozen onto orders; a BOOK of them is a new thing `Accounts` would own, with a default for checkout to preselect | `Accounts`, small |
| **Preferencias** | `Customer` already carries culture and currency; this is a screen over facts that exist, plus whatever notification settings arrive with a mailer | `Accounts`, small |
| **Cheques regalo** | a balance that is spent at checkout, so it is a second tender beside the payment port and it touches `Pricing` (it is not a discount), `Ordering` and the refund path. The interesting question is whether an unspent balance is a liability the shop tracks — it is | new, real work |
| **Mis pagos** | **not card data.** Storing a PAN is out of the question and the port has no tokenisation concept yet; what a shop keeps is a provider-side token and a masked label. It is a change to `IPaymentProvider` before it is a screen | `Ordering` + the port, medium |

**And this is the moment the account grows a sidebar.** One was declined on
2026-09-04 with the reason that Amazon has a menu because it has fifteen
sections and this had one — a menu with a single item is chrome pretending to be
structure. Three real sections is when it stops pretending, and addresses is the
one that gets it there.

**What to resist:** building the menu first. The order is a section, then a
second section, then the thing that lists them.

### A storefront that looks like a shop

**Reference: [warbyparker.com](https://www.warbyparker.com)** — the user's own,
recorded 2026-09-04. It could not be inspected when this was written (its server
answers 403 to a fetcher and no browser was connected), so nothing below claims
anything about how *they* do it. What is written down instead is what THIS shop
is missing, which is the half that matters when the work starts.

**The honest position today.** Every screen in the storefront is correct,
accessible, tokenised and tested. None of them is a shop. What exists is a
search box, a result grid, a product page, a cart and a checkout — the
transactional spine and nothing around it:

| gap | what is there today |
|---|---|
| **No home** | `/es` renders the search page. A shop's front door is the one screen that says what kind of shop it is, and there is nothing between the header and an empty result grid |
| **The taxonomy is invisible** | phase 2 built categories with a localized name, a parent and a materialized path, and the ONLY consumer is the search index. Nothing browses them, so "Hogar › Cocina › Menaje" exists as data and as a breadcrumb and never as a way in |
| **No imagery beyond the product shot** | and the seed's point at `cdn.example.com` on purpose, so a fresh clone is a grid of fallback tiles. The design has an editorial device — the awning stripe — used on exactly one `<hr>` |
| **No promise on the PDP** | `initial-plan.md` §2 already carries the number: **44% of sites do not show the return policy on the product page while ~60% of shoppers look for it there.** The returns machinery shipped in phase 5 and the page says nothing about it |
| **The empty states are the honest minimum** | "Nothing here yet" is correct and is not a reason to stay |

**What it is NOT.** Not a rewrite of `design/tokens.css`, and not a change to
`design/DESIGN.md`'s opinions — the awning stripe, the clay accent, one accent
action per view, Bricolage/Instrument Sans/JetBrains Mono and tabular numbers all
stay. The system is fine; it is barely used. This is a **page** problem, not a
token problem, and any proposal that starts by picking new colours has
misdiagnosed it.

**Why it is not scheduled.** Every other row in this table is deferred for
scale; this one is deferred for sequence. A shop that looks beautiful and cannot
tell you why a discount did not apply is the demo this project exists to argue
against — the floor first, then the surface. It also wants two things that do not
exist yet: real photography (the image derivatives row above) and something to
browse (facets, phase 8).

**Enters when** phase 8 gives the catalogue facets to browse by, or earlier if a
published article needs the screenshots to carry it — article 00 leads with a
live search box, and a front page nobody has designed is a poor first impression
for the piece that introduces the project.

**One caution for whoever picks this up.** Take the *shape* of a reference, never
its palette. The single most recognisable failure mode in 2026 design is the
warm-cream, high-contrast-serif, terracotta-accent cluster that DESIGN.md already
names and deliberately avoids — and it is exactly what "make it look like that
nice site" produces.

