# Tendero — initial plan

The complete record of the project's founding decisions. Everything here was
argued before it was accepted; when reality contradicts this document, write an
ADR and update it.

## 1. Vision and goals

Build **Tendero**, an AI-native ecommerce platform, as a public GitHub project
with three simultaneous goals:

1. **Learning**: strengthen software-architecture and AI-architecture skills
   (AI Engineer/Architect profile) with production-grade patterns, not demos.
2. **CV**: a repo that demonstrates judgment — measured search quality,
   human-in-the-loop AI, agent-ready commerce — rather than framework stacking.
3. **Blog**: every phase ships an article. English first (reach), Spanish on
   elguerre.com (home). The `blog-draft` skill generates both.

Constraints: everything self-hostable with Docker at zero cost; .NET 11
(preview is fine) + Aspire backend; Angular 22 frontends; domain = ecommerce to
align with the author's day job.

## 2. Market validation (researched Aug 2026)

Why ecommerce + AI is the right playground now:

- **Agentic commerce is the open frontier.** UCP (Universal Commerce Protocol)
  launched at NRF Jan 2026 (Shopify + Google + Walmart/Target/Etsy and 20+
  endorsers), covering catalog search, cart, identity, checkout, order
  management, with MCP/A2A/AP2 support built in. AP2 (payment consent via
  cryptographic mandates) was donated to the FIDO Alliance in April 2026.
  Official reference implementations exist in **Python and Node.js only — there
  is no .NET reference implementation**. That gap is Tendero's headline
  contribution.
- **Returns are the money pain.** ~20.8% average online return rate (2–3× brick
  and mortar), $10–65 processing cost per return; fit/sizing causes ~50% of
  apparel returns. Meanwhile 44% of sites don't show return policy on the PDP
  and most fashion sites lack sufficient sizing info — while ~60% of shoppers
  look for it there. Grounded pre-purchase Q&A and closed-loop return-reason
  analysis are high-value, under-built features.
- **Multimodal search is table stakes by 2026**, not a differentiator: build it
  as a capability (phase 4), not the headline.

## 3. Product scope

- Own commerce plane: Tendero **owns catalog, cart, checkout, orders** (no
  delegation to an external platform). Order is a real aggregate with a state
  machine and compensation saga.
- Two Angular apps in one Nx workspace with shared libs:
  - **Storefront** (deliberately minimal): home, search, PDP, cart, checkout.
  - **Backoffice** (where the project really lives): AI review queue, search
    evaluation panel, AI cost/latency observability, orders + return reasons,
    **agent activity panel** (what each UCP agent did, which AP2 mandate it
    presented, what was denied).
- **Multilanguage (es/en) by design** across domain, indexes, and both apps.

## 4. Architecture

Three planes (see docs/architecture.md for detail):

1. **Inbound connectors** — N catalog sources behind ONE port
   (`ICatalogSourceConnector`), keyed DI by source name:
   `seed` (default, ABO-style JSON in repo) · `shopify` (free dev store, SaaS)
   · optional `medusa`/`vendure` (Docker) · `woocommerce`/`prestashop` ·
   Google Merchant feed. Contract-test suite every adapter must pass.
2. **Core** — canonical model, two bounded contexts (`Catalog`, `Ordering`),
   vertical slices (Carter + FluentValidation + custom CQRS dispatchers),
   Outbox for all side effects, OpenTelemetry everywhere (GenAI semantic
   conventions for LLM calls).
3. **Exposure** — Angular apps over HTTP; **UCP + MCP server** for agents.
   Shopify reduces UCP's value if it's the commerce engine (Shopify rolls out
   UCP for its merchants automatically) — so Shopify is a *catalog source*,
   never the engine.

Payments: one port, three adapters — `FakePaymentProvider` (in-process,
configurable failures; default), `stripe-mock` (Docker, contract tests only),
Stripe test mode (real webhooks via Stripe CLI). Same contract-test suite.

Infra (all Docker, orchestrated by Aspire): Postgres (source of truth),
Elasticsearch (BM25), Qdrant (vectors), Redis (cache/queues), Ollama (local
LLMs + embeddings; bge-m3 planned for multilingual vectors), OTel → Grafana
stack + Langfuse (self-hosted).

## 5. AI layering policy

- **Microsoft.Extensions.AI** for pipelines (enrichment, translation,
  embeddings): deterministic, testable, no agent framework.
- **Microsoft Agent Framework** (1.0 GA Apr 2026; successor of Semantic Kernel
  + AutoGen) only when a real agent exists: the phase-3 shopping assistant with
  tool calling.
- **No Agent Framework Harness**: it targets long-running autonomous agents
  with shell/filesystem access; Tendero's agents are short-lived and
  constrained. "When NOT to use the harness" is itself an article.
- **No LangChain/AutoGen/LlamaIndex in .NET services.** A Python sidecar exists
  only for evaluation (Ragas/DeepEval) and later document ingestion (Docling).

## 6. Search plan

1. `Catalog/ImportProducts` — seed connector, idempotent upsert by
   (source, externalId), batched, Outbox events. ✅ designed
2. `Search/Lexical` — BM25, **one index per language** (`products_es` with
   `spanish` analyzer, `products_en` with `english`), multi_match with boosts
   (name^3, brand^2, attributes^2), AUTO fuzziness, AND operator, status filter.
   Permanent fallback. ✅ designed
3. `Search/Indexing` — embeddings worker (Ollama → Qdrant), idempotent, full
   reindex on model change.
4. `Search/Hybrid` — RRF fusion + optional rerank, feature-flagged per layer.
5. `Evaluation/GoldenSet` — 50–100 annotated queries per culture (relevance
   0–3 with a WHY comment), NDCG@10 + recall@50 computed in CI, thresholds
   committed; regressions fail the PR.

Dataset: **Amazon Berkeley Objects** (~147k products, multilingual metadata,
images, CC BY 4.0). Repo ships a small es/en sample in the same shape; a script
downloads and converts the full set.

## 7. Phases and blog articles

| Phase | Slices | Article |
|---|---|---|
| 1 | Import + Lexical + GoldenSet | "One port, N catalogs"; "Search with regression tests: NDCG as a CI gate" |
| 2 | Hybrid search; AI enrichment + translation with review queue; Shopify connector | "Hybrid search in .NET"; "Human-in-the-loop is a UI, not a slogan" |
| 3 | Checkout + order saga + payment adapters; UCP + MCP server | "A UCP merchant server in .NET (the missing reference implementation)"; "When NOT to use the agent harness" |
| 4 | Multimodal (image embeddings via ONNX/CLIP-class models); grounded post-sale Q&A; return-reason loop | "Search by photo, measured"; "Closing the returns loop with an LLM" |

Deferred on purpose (each enters when its absence hurts; each is an article):
product variants, multi-currency, inventory as its own aggregate, localized
attribute values, import-error table in backoffice, Vendure adapter, Docling
ingestion, Kubernetes/Azure deployment.

Also deferred, and recorded when the decision was taken: **image derivatives**
(thumbnails, WebP/AVIF per size). A real shop generates them; Tendero serves the
original. It is a layer over the same `IImageStore` port, not a change to the
model, and the URL shape already accommodates it (`/api/images/{key}?w=400`), so
nothing has to be undone. With six 640px images there is nothing to measure, and
the choice it forces — generate on upload, on demand with a cache, or at the CDN
edge — depends on a deployment target that is itself deferred. Lands with the
full ABO import.

Also pending, decided but not done: **prefix the .NET namespaces with the
author's**, so `Tendero.Catalog` becomes `ElGuerre.Tendero.Catalog` and the root
convention in CLAUDE.md becomes `ElGuerre.Tendero.*`. It is a mechanical rename
but it is not small: 12 projects, their assembly names, every `using`, the
`InternalsVisibleTo` in Catalog, and the architecture tests that build namespace
strings from assembly names. Best done in one commit that touches nothing else,
so the diff stays reviewable. Open question when it lands: whether the npm scope
in `frontend/` follows (`@tendero/*` → `@elguerre/tendero-*`) or stays as is —
npm scopes and .NET namespaces do not have to agree, and `@tendero` is shorter.

Also deferred, with the measurement already taken: **outbox event coalescing.**
`Product` raises a `ProductUpserted` on every mutation, so one imported product
produces roughly eight of them — measured at 47 outbox rows for the 6-product
seed sample. It is correct (the projection is idempotent) and irrelevant at this
size, but the full Amazon Berkeley Objects set would write ~1.2M rows to do
147k products' worth of work. The fix belongs in the drain to the outbox, not in
the aggregate: the domain is right to record what happened, and delivering the
same "this product changed" eight times for one transaction is a delivery
concern. It needs care, because collapsing by event type is only safe for
state-snapshot events — two `OrderPaymentFailed` with different reasons in one
unit of work are two facts. This lands with the full-dataset import script,
which is when there are before/after numbers worth publishing.

## 8. Engineering practices

- Testing: see docs/testing.md. xUnit v3, NSubstitute, builders + Bogus (no
  AutoFixture), Verify snapshots, CsCheck property-based, Testcontainers +
  Respawn, NetArchTest architecture rules, SearchEval CI gate.
- .NET 11 preview via global.json (`allowPrerelease`) + `dotnet-quality:
  preview` in CI; monthly-preview breakage is accepted (and bloggable).
- Licensing: only permissive OSI licenses (MIT/Apache/BSD), verified per
  package version. Banned: MediatR, AutoMapper, MassTransit v9+,
  FluentAssertions v8+, Moq (all moved to commercial/dual licensing in
  2025-2026 or had trust incidents). This is why the CQRS dispatcher, mapping
  and Outbox are hand-written.
- Spec-driven development: lightweight — CLAUDE.md as constitution, one spec
  per feature in docs/specs/, ADRs for constraining decisions. Kiro discarded
  (separate IDE, leaves Claude Code). Spec Kit was evaluated once phase 1 was
  built: lightweight stays through phases 1-2, and Spec Kit runs as a
  controlled experiment on the phase-3 UCP server, with the comparison
  published as the article (ADR 0009).
- Claude Code setup: CLAUDE.md short + @docs imports; repo skills:
  `new-slice`, `run-search-eval`, `write-adr`, `blog-draft`; MCP servers:
  GitHub, Playwright (when Angular work starts).

## 9. Brand and design

- Name **Tendero** ("shopkeeper"); brand "Tendero Commerce"; tagline
  *commerce for humans and agents*. Runner-up considered: Bodega, Zócalo;
  Zoco discarded (phonetic clash with Zoko).
- Mark: a **T whose crossbar is a striped shop awning** (brand/). Terracotta
  `#D85A30` primary; favicon drops stripes below 24px.
- Design system (design/): tokens.css single source of truth; Bricolage
  Grotesque display / Instrument Sans body / JetBrains Mono for all numbers
  (tabular); the **awning stripe** as the only decorative device; dark mode in
  backoffice only; **agent-driven actions always render in clay**; one accent
  action per view; es/en copy via Transloco, never hardcoded.

## 10. First tasks in Claude Code

1. Generate the solution skeleton that satisfies CLAUDE.md (projects, custom
   CQRS abstractions, Aspire AppHost with Postgres/ES/Qdrant/Redis/Ollama).
2. Wire the existing slices (Import, Lexical) into real projects; make the
   contract tests and a first architecture-test project pass.
3. tools/SearchEval skeleton + golden/{es,en}.json with ~20 starter queries.
4. Nx workspace with storefront + backoffice shells using design tokens.
5. docs/adr: accept the five founding ADRs (drafted in docs/adr/).
