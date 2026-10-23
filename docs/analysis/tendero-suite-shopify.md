# Tendero and Shopify: leaving it, selling through it, or a suite on it

**Revisited 2026-10-23** (sections 9 to 16; S1 to S3 became phase 16 the same day, S1 specified in [`../specs/shopify-import.md`](../specs/shopify-import.md)), at the author's request, once phase
10 had long closed — the trigger §8 set. The author widened the question from
*a suite on top of Shopify* to the cases a real merchant brings, for the blog
series: *"real cases that can happen, for our series; always useful cases, and
why not, real ones"*. Two are new: **a merchant leaving Shopify for Tendero**
(§10) and **Shopify as the storefront with Tendero as the engine** (§11). Both
run on one shop and need no multi-tenancy (§9); a phased plan is §14. Sections 1
to 8 are the first analysis, unchanged.

Opened 2026-09-26 at the author's request, analysed 2026-09-27 after `P10-0`
closed. The question: *our backoffice is everything today, but we could split it
and sell it as products of a suite — and a customer might already have Shopify
and not want to pay for our storefront.* The author's leaning is *optional*, and
*maybe we can postpone it*: this document is what it would need, so that choice
is made on the plan rather than on the idea. Decisions are recorded with their
date and who took them, as in [`claim-extraction-models.md`](claim-extraction-models.md).

## 1. What it reopens

`initial-plan.md` decided on day one: *Shopify is a catalog source, never the
engine* — because Shopify gives its merchants UCP by itself, and Tendero's first
differentiator, the agent-native merchant, would be worth little on top of it.
That reason still holds **for the agent surfaces** (MCP, UCP, Know Your Agent).
It does not obviously hold for the modules that are not about agents.

## 2. What Shopify merchants have today (researched 2026-09-27)

- **Shopify Magic** suggests a product's category in the Standard Product Taxonomy
  and, from it, the category's attributes ([Charle](https://www.charle.co.uk/articles/product-taxonomy/)).
- **Category metafields** (the taxonomy's attributes) are writable through the
  Admin GraphQL API — `productSet`, `metafieldsSet` ([Shopify](https://shopify.dev/docs/apps/build/metafields/manage-metafields)).
- **Shopify Knowledge Base**, free and first-party: FAQs generated from the
  catalogue for the AI agents that answer shoppers, which the merchant can view,
  edit and extend, with a log of what agents asked ([App Store](https://apps.shopify.com/shopify-knowledge-base)).
  Its reviews name the gap exactly: *"there's no control over AI hallucination, and
  the tool sometimes invents answers"*; *"we have no way to select or correct the
  data source used for a FAQ"*.
- **Third-party apps** extract attributes into metafields with AI — GEOly
  ([App Store](https://apps.shopify.com/geoly-catalog-optimization)) among them —
  writing the final value.
- The pressure behind it: Shopify reports AI-driven traffic to its stores up 8×
  and orders from AI searches up ~13× year on year in Q1 2026, and sells through
  AI conversations with *Agentic Storefronts* ([Shopify](https://www.shopify.com/blog/how-agentic-commerce-works)).

**Correction to the first reading** (2026-09-26 said *"Shopify doesn't do this"*,
already narrowed that day): Shopify now helps merchants review what agents are
told, at the level of store FAQs, and suggests attributes. What none of the above
shows is **each fact tied to the sentence it comes from, with a support score and
an approval, and contradictions between text and fields surfaced** — which is
what phase 10 builds, and what the Knowledge Base's own reviewers ask for.

## 3. What each module would be worth on top of Shopify

| module | worth on Shopify | why |
|---|---|---|
| **Product reasoning layer** (claims, support, approval, contradictions — phase 10) | **the strongest** | the gap the Knowledge Base's reviews describe; published back as category metafields and metaobjects, it reaches Shopify's search, filters and agents |
| PIM (localized attributes, taxonomy, review queue) | some | established competition (Akeneo, Plytix, Salsify) |
| Hybrid search | little | replacing a store's search is invasive; Shopify has Search & Discovery |
| Backoffice copilot (phase 12) | little | Shopify Sidekick |
| MCP / UCP / Know Your Agent | none | Shopify provides them and owns the checkout — the day-one decision stands |
| Evaluation and AI observability | niche | valuable to few merchants |

## 4. What it would take — three levels

**Level 0 · a demonstration on a development store** — *M to L*, after phase 10.
- A free Shopify development store, a custom app with one store's token.
- **Inbound**: the Shopify adapter of `ICatalogSourceConnector` — already in the
  standing backlog, and worth having on its own (a second real source proves the
  port; ADR 0003).
- **Outbound**: a port the solution does not have — publishing to a channel —
  with a Shopify adapter writing approved claims as metafields under a Tendero
  namespace. The same port would later carry a Google Merchant feed.
- A rule of **which system owns which data**: catalogue text in Shopify, claims
  and their evidence in Tendero, approved values mirrored back.
- Client: ShopifySharp 6.29.0 is MIT (it brings Newtonsoft.Json), or the Admin
  GraphQL API over `HttpClient` with no dependency — a decision for the day.
- Outcome: an article, *Knowledge for Shopify*, and the proof that phase 10 works
  on someone else's catalogue.

**Where the module would live on Shopify** (the author asked, 2026-09-27: *does
Shopify have its own backoffice?*). It does — **Shopify Admin**, with Sidekick,
its AI assistant, inside it (Steward's counterpart). Apps are embedded in the
Admin through App Bridge. So a Shopify merchant would review claims **inside
Shopify Admin**, as an embedded app — never in Tendero's backoffice, which only a
merchant running Tendero whole would use. Level 0 can still use the backoffice;
from Level 2 on, the review queue has to exist as an embedded screen.

**Level 1 · one real merchant, privately** — *L*.
- Webhooks (`products/update`) to re-extract on change, rate limits, retries,
  per-store configuration and secrets.
- Tendero **hosted somewhere reachable** — today it runs on a laptop only.

**Level 2 · the Shopify App Store** — *XL, and a change in what the project is.*
- OAuth install flow and **multi-tenancy**: every table, queue, index and model
  budget scoped by shop. The scale rule keeps one shop today.
- An embedded admin (App Bridge, Polaris), the **Billing API** for any charge,
  the three **mandatory privacy webhooks** (`customers/data_request`,
  `customers/redact`, `shop/redact` — the most common first rejection), and a
  review of 2–4 weeks ([Shopify](https://shopify.dev/docs/apps/launch/shopify-app-store/app-store-requirements)).
- Operating a SaaS: hosting, uptime, support, GPU or CPU budget per tenant for
  the extraction model (Qwen3.5-4B is ~5 GB and ~30 s per product on CPU).

## 5. What it depends on

- **Phase 10 complete** — claims, the review queue, approval, the Catalog
  projection. Level 0 publishes approved claims; before they exist there is
  nothing to publish.
- Nothing in phases 11 or 12. Phase 12b (the shopping assistant) is Tendero's
  storefront, and irrelevant to a Shopify merchant.

## 6. Risks

- **Platform risk.** Shopify is moving into the space (Knowledge Base, Magic);
  a first-party feature that adds source control would close the gap for free.
- **Scope.** Level 2 turns a laboratory into a product company. The scale rule
  exists to keep architectural completeness separate from operational scale.
- **Dependency.** ShopifySharp would be the project's first Newtonsoft.Json.

## 7. Recommendation, for the author

**Postpone the suite; keep it optional; do not add a phase yet.** Nothing in it
is needed before phase 10 is complete, and Level 2 is a different project.

What keeps the option open at no cost, and is worth doing anyway:
1. **When phase 10's approval flow exists**, design the **outbound channel port**
   with Level 0 in mind — the Google Merchant feed and a Shopify store are the
   same shape.
2. **The inbound Shopify connector** stays in the standing backlog as it was, now
   with a second reason.
3. **Revisit after phase 10 closes**, with Level 0 as the candidate spike: a
   development store, one article, and the answer to *does the claims layer work
   on a catalogue we did not write?*

## 8. Decisions

| date | decision | by | why |
|---|---|---|---|
| 2026-09-26 | Analyse Tendero as a suite with Shopify as a channel, after `P10-0`, to decide whether it enters the plan as an optional line | the author | *"maybe it would be optional; let's see the analysis first"* |
| 2026-09-27 | **Postponed, optional, no phase yet.** Revisit when phase 10 closes, with Level 0 as the candidate spike; meanwhile design phase 10's outbound publication with a channel port in mind, and keep the inbound Shopify connector in the backlog | the author, on §7 | nothing in it is needed before phase 10 is complete; Level 2 would turn the laboratory into a SaaS company; Shopify is moving into the space, so the case is re-measured then, not assumed now |
| 2026-10-23 | **Revisited, widened to three cases**: leaving Shopify (§10), Shopify as the storefront over Tendero's engine (§11), and the suite of §1–7 (now §12). For the series, on cases a real merchant brings; no client behind it today | the author | *"casos posibles reales que se pueden dar, pero es para nuestra serie"* |

## 9. Shopify is multi-tenant; Tendero is one shop, and neither new case needs more

**Shopify** serves every merchant from shared infrastructure. Each store is a
`shop_id`; since 2016 shops are grouped into **pods**, each a set of shops on a
fully isolated set of datastores, while the stateless application workers are
shared ([Shopify Engineering](https://shopify.engineering/a-pods-architecture-to-allow-shopify-to-scale),
[InfoQ](https://www.infoq.com/presentations/shopify-architecture-flash-sale/));
`shop_id` is the shard key, and shops move between shards online
([Shopify Engineering](https://engineering.shopify.com/mysql-database-shard-balancing-terabyte-scale)).
Most of that detail comes from 2016 talks; the principle is firm, the internals
may have moved. Shopify Plus also lets one organisation run several stores.

**Tendero** is one shop, by the scale rule: no table, index or queue carries a
tenant. The decision is a standing-backlog row since 2026-09-27 (database,
schema or row-level security per tenant; an index or a filter per tenant in
Elasticsearch; Keycloak's organisations as the source).

**Neither new case needs it.** A merchant who leaves Shopify runs *their*
Tendero; a merchant who keeps Shopify's storefront over Tendero's engine runs
*their* Tendero too. One deployment per merchant, as today. Multi-tenancy is
needed only by Level 2 of §4 — Tendero as a SaaS serving many merchants — and
that stays a different project.

## 10. Case A — leaving Shopify for Tendero

A merchant imports everything once, checks it, switches the domain over, and
closes the Shopify store. It is the inbound port's natural use, at its fullest.

| what | difficulty | notes |
|---|---|---|
| **Catalogue**: products, variants, options, images, metafields | low | the `ICatalogSourceConnector` port and its contract suite (ADR 0003). `ExternalProduct` already carries `Variants`, but an `ExternalVariant` is a key and its attributes only: a Shopify variant brings its own **id, SKU, barcode, price and stock**, so the contract grows, and `VariantExternalReferences` (P1-2, waiting for exactly this connector) gets built. Shopify allows **2,048 variants per product** for every merchant since 2025-10-15 ([changelog](https://shopify.dev/changelog/the-product-variant-limit-is-now-2048-for-all-merchants)) |
| **Collections → categories** | medium | a collection is not a tree, and an automated one is a rule. The mapping is proposed and a person reviews it; Shopify's Standard Product Taxonomy category, when set, is the better source |
| **Translations** | medium | a store's other languages (Translate & Adapt) become `LocalizedText`; a single-language store gets the missing ones from the enrichment slice, as the contract already says |
| **Prices** | low | a price list in `Pricing` |
| **Stock per location → warehouses** | low | Shopify locations become warehouses; quantities through `Inventory`'s own commands |
| **Customers** | high | **passwords cannot be exported** — no platform hands over its hashes; migrations send account invites ([Matrixify](https://matrixify.app/tutorials/migrate-customers-new-shopify-store-activate-new-accounts/)). Here: customers created in Keycloak without a password, with a required action to set one on first sign-in, or an invitation. Personal data: a lawful basis, and the customers told |
| **Order history** | high | imported **already closed**, lines snapshotted as `OrderLine` already does. It meets invariant 5 (*a status changes only through `TransitionTo`*): an imported order does not change status, it is **born** in one — an ADR of its own, so that the exception is named and narrow |
| **Discounts** | high | translated into the promotion engine only where they fit; the rest listed for the merchant |
| **Gift cards** | high | the full code is **returned once, at creation**; afterwards the API gives only its last characters ([Shopify REST reference](https://shopify.dev/docs/api/admin-rest/2024-04/resources/gift-card), [Shopify staff](https://community.shopify.dev/t/need-to-access-full-gift-card-codes-for-physical-gift-card-fulfillment/20947)). Balances can move, codes cannot: new codes, sent to their holders |
| **Tax class per variant** | medium | added 2026-10-23: Shopify's `taxable` flag and its tax overrides per collection and country, mapped to `Variant.TaxClass` (standard, reduced, super-reduced, exempt). S1 does not bring it. A store selling in several EU countries needs rates by destination, which Tendero does not have yet (the board's backlog, *Tax by product code and destination*) |
| **URLs and SEO** | **critical** | 301 redirects from `/products/<handle>`, `/collections/<handle>`, `/pages/…` and `/blogs/…` to Tendero's routes. Without them the store loses its search ranking on the day it switches — the step most migrations forget |
| Reviews | out of scope at first | they live in third-party apps, not in Shopify |

**A tension with our own rule.** *Importing never publishes* (invariant 5, ADR
0012): an import lands as Draft for the review queue. Hundreds of products that
were on sale yesterday cannot wait for one review each. The answer is a
**bulk publication**, explicit and audited — a person decides "publish what was
migrated", the import never does — which keeps the rule's point: the machine
proposes, a person approves.

**A rehearsal before the day.** The migration runs first as a dry run that
reports what it could not bring (a discount it cannot express, a gift card, a
collection with no category), so the merchant decides before switching, not
after.

## 11. Case B — Shopify as the storefront, Tendero as the engine

The merchant keeps paying for Shopify and keeps its storefront and checkout;
Tendero is the system of record behind it. This is the reverse of the day-one
rejection: what §1 rejected was Tendero **on top of** Shopify's engine; here
Shopify is a **sales channel** fed by Tendero's.

| data | master | in Shopify |
|---|---|---|
| catalogue, variants, attributes, taxonomy, translations | **Tendero** | a published copy |
| stock | **Tendero** (reservations, warehouses) | a copy, decremented on a sale and reported back |
| prices | **Tendero** | a copy |
| promotions | **to decide** | Shopify has its own |
| cart, checkout, payments, taxes | — | **Shopify** |
| orders | **Tendero**, received by webhook, then our state machine | created there |
| customers | **to decide** | Shopify has its own |

**How it fits.** Symmetric to what exists: an **outbound port for sales
channels** beside the inbound one for catalogue sources, Shopify its first
adapter (a Google Merchant feed and a marketplace later, as §7 foresaw).
Publishing is domain events → outbox → a worker → Shopify (invariant 7), never
inline; orders arrive through **idempotent webhooks** (`orders/create`), the way
the agent checkout's idempotency already works. No bounded context changes.

**What is genuinely hard.**
1. **Two copies of stock.** Shopify decrements on a sale, Tendero too, with a
   delay between them: a window for **overselling**. A safety margin per
   variant, and a rule for who wins when they disagree.
2. **Promotions.** The engine's combination rules are not Shopify's. Three
   options: leave promotions in Shopify for that channel; translate the ones
   that fit; or **Shopify Functions** asking Tendero's rules.
3. **Customers and personal data**, which would otherwise live twice. In this
   case `Accounts` may hold only the customer an order brings.
4. **API budget.** GraphQL calls cost points against a bucket restored per plan
   — 100 points a second on the standard plan, 1,000 on Plus — and one query may
   cost at most 1,000; **bulk operations** have no such limits and are the way
   to read or write a whole catalogue ([Shopify](https://shopify.dev/docs/apps/build/apis/graphql-admin/rate-limits)).
   `productSet` takes at most 50,000 inventory quantities per mutation since
   2025-10 ([changelog](https://shopify.dev/changelog/posts/productset-limit-for-inventory-quantities.md)).

**What survives of the differentiators in that channel.**
- **Kept**: the product reasoning layer — approved claims enrich the product
  Shopify shows, as category metafields (§3, the strongest case still); Steward,
  because the backoffice is Tendero's; evaluation and observability.
- **Only as an app in the theme**: Tino, the explainable kit and the search the
  NDCG gate measures (article 03). Without it the shopper uses Shopify's search.
- **Lost**: UCP, the agent checkout and Know Your Agent — Shopify gives them to
  its merchants and owns the checkout. The day-one reason holds, for this channel.

**Our storefront stays.** It is the reference channel — the series and the
agent-native claims rest on it. Shopify is a second channel for the merchant who
already has one; the same platform serves both, and an adapter proves it, which
is the thesis of the series.

## 12. Case C — a suite on Shopify

§1 to §7 unchanged: approved claims written into a Shopify store as an app.
Level 0 and 1 are one shop; **Level 2, the App Store, is where multi-tenancy
(§9) becomes unavoidable**, with OAuth installs, billing and the mandatory
privacy webhooks. It stays postponed.

## 13. What the cases share

One Shopify client and one mapping serve A, B and C: the Admin GraphQL API over
`HttpClient`, or ShopifySharp (MIT; it brings Newtonsoft.Json) — a dependency to
ask for, not assume. A free **development store** with sample data tests all
three. The order of the work follows from it: the client and the mapping are
built once, for the case that needs the least else.

## 14. Phases

Each phase stands on its own, ends with something a merchant could use, and is
an article.

| phase | what | builds | article |
|---|---|---|---|
| **S1 · Import the catalogue from Shopify** | the Shopify adapter of `ICatalogSourceConnector`, variants with their own ids, SKUs, prices and stock; collections proposed as categories; translations | the Admin GraphQL client, bulk reads, the mapping; `ExternalVariant` grows; `VariantExternalReferences`; the contract suite on a second real source | *A second real source: importing a Shopify store through the same port* |
| **S2 · Leave Shopify** (case A complete) | prices, stock per location, customers without passwords (Keycloak's required action), order history born closed, discounts that fit, gift cards reissued, 301 redirects; the dry run's report; bulk publication | an ADR for imported orders; an ADR or an amendment for bulk publication; a redirect table the storefront serves | *Leaving Shopify without losing your search ranking* |
| **S3 · Shopify as a channel** (case B, without the theme) | catalogue, stock and prices published; orders received; the stock margin | the outbound sales-channel port; workers on the outbox; idempotent webhooks; an ADR on which system owns which data | *Shopify in front, your engine behind* |
| **S4 · Tendero inside the Shopify theme** (optional) | Tino and the measured search as a theme app extension | an app block calling Tendero's API | depends on S3's evidence |
| **S5 · The suite** (case C) | approved claims as metafields; Level 2 only with multi-tenancy decided | the channel port of S3, carrying claims | the *Knowledge for Shopify* article of §4 |

**Why this order.** S1 is already expected by the code (ADR 0015, P1-2's
`VariantExternalReferences`) and proves the port with a real source; S2 reuses
S1's client and is the case with the clearest value for a reader. S3 needs a
port that does not exist yet, and decisions (stock, promotions, customers) that
S1 and S2 inform. S4 and S5 wait for evidence.

## 15. Decisions each phase needs first

- **S1**: the client — `HttpClient` or ShopifySharp (dependency policy); how a
  collection becomes a category.
- **S2**: imported orders born in a final status (ADR); bulk publication of a
  migration (ADR 0012 amended or a new one); customers' lawful basis and how they
  are told.
- **S3**: who owns stock, and its margin; where promotions live in the channel;
  whether customers are copied.

## 16. Recommendation, 2026-10-23

Phase the two new cases, A before B, starting with **S1**: it is the smallest,
the code already waits for it, and it makes the client and the mapping every
later phase reuses. It does not compete with phase 15 for the same code (it
touches Catalog's connectors, not Accounts), so the author can place it before,
after or beside it. Nothing here adds a dependency or a context without asking.
