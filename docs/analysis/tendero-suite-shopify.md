# Tendero as a suite: Shopify as a channel

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
