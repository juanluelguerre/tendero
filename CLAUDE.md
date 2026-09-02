# CLAUDE.md — Tendero

The AI-native shopkeeper: an ecommerce platform for humans and AI agents.
.NET 11 (preview) + Aspire backend, Angular 22 frontends, self-hosted AI stack
(Ollama, Qdrant, Elasticsearch) via Docker. Companion repo for a blog series —
code quality and architectural clarity matter more than shipping fast.

Root namespace: `ElGuerre.Tendero.*` (`ElGuerre.Tendero.Catalog`,
`ElGuerre.Tendero.Ordering`, `ElGuerre.Tendero.Search`,
`ElGuerre.Tendero.SharedKernel`, `ElGuerre.Tendero.Ucp`). Project file name,
assembly name and root namespace are the same string; the folders under `src/`
drop the prefix (`src/Catalog/ElGuerre.Tendero.Catalog.csproj`). The npm scope in
`frontend/` stays `@tendero/*` — npm scopes and .NET namespaces do not have to
agree, and the short one is the one people type.

## Product vision

Tendero has to be two things at once: a **credible commerce platform** and an
**agent-native** one. The second is the differentiator, and it is only credible
if the first is real — an agent checkout over a pretend cart demonstrates
nothing.

**The floor** (table stakes, and the substrate everything else needs): catalogue
with variants and structured, localized attributes · a localized taxonomy ·
prices, price lists and promotions with combination rules · real-time inventory ·
cart and checkout with taxes and shipping · orders with a state machine, and
returns · accounts and guests · idempotent payments with webhooks · faceted
search · SEO · a backoffice with roles · audit · observability.

**Standard 2026 AI** (expected, not differentiating): hybrid search, visual
search, multimodal ingestion, a conversational assistant, personalization.

**The six differentiators**, in priority order:

1. **Agent-native merchant** — `/.well-known/ucp`, UCP capabilities and an MCP server. There is no .NET reference implementation of UCP; that gap is the project's headline contribution.
2. **Product reasoning layer** — structured knowledge per product (specs, evidence, comparisons) carrying **source and confidence**. The AI proposes, the shopkeeper approves.
3. **Know Your Agent** — identify legitimate agents (signatures, AP2-style mandates) and make anti-fraud rules agent-aware.
4. **Backoffice copilot** — natural language over our own data and OpenTelemetry traces, proposing **approvable actions**.
5. **Explainable, constrained recommendations** — "a kit for under X", showing its arithmetic.
6. **AI observability and evaluation as a feature** — cost per conversation, evals, a trace for every decision.

**Three agent surfaces, three trust models.** WebMCP runs in the user's browser
and **inherits** their session, so it needs no second principal. The MCP server
is external and read-only, `AllowAnonymous` by explicit decision. UCP + AP2 is
server-to-server with its own principal and a signed mandate. They are not
alternatives; each answers a different question.

### Scale rule

**Architectural completeness is total; data volume is laboratory scale.** These
are different axes. If promotions need real combination rules, build them. If
checkout needs a saga with compensation, build it. But the catalogue stays at
six products, warehouses at two, roles at three. Work that exists only because of
scale — outbox coalescing, bulk indexing, background reindex, image derivatives —
is **deferred with its measured number recorded**. Nothing is cut for being
laborious.

## Commands

```bash
dotnet run --project src/AppHost              # full stack via Aspire
dotnet build -warnaserror                     # warnings are errors, always
dotnet test                                   # unit + contract + architecture
dotnet test --filter Category=Integration     # Testcontainers (skips without Docker)
dotnet tool restore                           # EF tools, pinned to the runtime
dotnet tool run dotnet-ef migrations add <Name> --project src/Persistence
dotnet run --project tools/SearchEval         # golden set NDCG report
npx nx serve storefront|backoffice            # from frontend/

UPDATE_OPENAPI=1 dotnet test tests/Api.Tests  # regenerate docs/openapi/tendero.json
npm run generate:api-types                    # from frontend/, reads that document
```

The last two go together, in that order: the contract test regenerates the
document from the running API, and the frontend types are generated from the
document. Run both after changing anything a response returns; CI fails on either
being stale.

## Architecture invariants (never break these)

1. **Bounded contexts**: `Catalog` and `Ordering` never share entities. Orders
   snapshot product name/price into `OrderLine` — no live references.
2. **Vertical slices**: one folder per feature under `Features/`. Slices never
   reference other slices; shared behavior goes down (SharedKernel) or out (ports).
   Reference pattern: `Catalog/Features/ImportProducts`.
3. **Ports and adapters**: external systems (catalog sources, payments, search,
   LLMs) are reached ONLY through ports. Adapters register as keyed services.
   New connector = new class + DI registration, zero `if`s in handlers.
4. **Domain purity**: `*.Domain` code references SharedKernel only. No EF,
   no HTTP, no Elastic types. Architecture tests enforce this — run them.
5. **State machines are tables**: `Order.AllowedTransitions` is the single source
   of truth. Never add a status change outside `TransitionTo`. In Catalog,
   **importing never publishes**: Draft is the review queue's reason to exist,
   and only `PublishProduct` moves a product to Active (ADR 0012).
6. **Everything user-facing is `LocalizedText`** (es/en). Never store a bare
   string for name/description. Resolution: requested culture → en → first.
   An endpoint picks its culture as `?culture=` → `Accept-Language` → `es`, and
   answers with `Content-Language` + `Vary: Accept-Language` (ADR 0013). The
   explicit parameter wins because an agent has no browser locale.
7. **Domain events → Outbox → workers.** Handlers never index/email/call LLMs
   inline in a request. Raise the event; the worker projects.
8. **Lexical search is the permanent fallback.** AI features must degrade
   gracefully; never make BM25 depend on Ollama/Qdrant being up. Generalised:
   **every AI feature has a non-AI path, and the degradation is tested** — not
   asserted in a comment. In the browser this means feature-detecting
   `navigator.modelContext`; without it the shop is unchanged.
9. **AI layering**: Microsoft.Extensions.AI for pipelines, Microsoft Agent
   Framework only for real agents (phase 3 shopping assistant), no Agent
   Harness (our agents are short-lived and constrained).
10. **The identity provider is a port; we only ever validate tokens.** The
    contract is OIDC (discovery, JWKS, signed JWT), so the adapters are a
    development `dev-issuer` first and Keycloak later — the `FakePaymentProvider`
    pattern applied to identity, with a shared contract suite. **Nothing is
    anonymous by default**: public endpoints carry an explicit `AllowAnonymous`.
    Never write an authorization server as a feature. One issuer, two kinds of
    principal: a person and an agent — and an agent is a principal, not a
    customer.
11. **OpenAPI is the single source of the API's shape.** The committed document
    generates the frontend's TypeScript types and backs the UCP capability
    schemas. Never hand-write a DTO mirror again.
12. **AI proposes, humans approve, the system applies.** A model never writes to
    an aggregate. It writes a claim or a proposed command, carrying its source,
    confidence and provenance; approval is a domain operation with an audit row,
    never a chat message. Anything a model decides that could be decided
    deterministically, is: the model parses and phrases, code chooses.

> **Open, not yet decided:** the roadmap takes the context count from two to six
> (`Catalog`, `Pricing`, `Inventory`, `Ordering`, `Accounts`, `Knowledge`).
> Invariant 1 and ADR 0002 still say two, and they stay that way until an ADR
> generalises the principle — contexts share no entities — instead of the count.
> See @docs/analysis/roadmap.md. Do not quietly add a context before that ADR.

## Conventions

- C# 14, file-scoped namespaces, primary constructors, collection expressions.
- Strongly-typed ids (`ProductId`, `OrderId`) — never raw Guids in signatures.
- Custom CQRS abstractions (`ICommand`/`IQuery` + dispatchers), NOT MediatR-the-package.
- FluentValidation per slice; Carter modules for endpoints.
- OpenTelemetry: every slice with I/O gets an `ActivitySource` span with tags.
  GenAI calls follow the OTel GenAI semantic conventions.
- UI: tokens from `design/tokens.css` only — never hardcode a hex in components.
  One clay accent action per view. Agent-driven actions are always clay.
- Solution format: Tendero.slnx (XML). Never create or commit a legacy .sln.

## Git Workflow

- Main branch for PRs: develop
- Feature branches: feature/{feature-name}
- Commit format: Conventional Commits style:
  - DO NOT add `Co-Authored-By` lines to commit messages
  - Write clear, descriptive commit messages in English
  - Use conventional commit style when appropriate:feat(brands):, fix(auth):, chore(build), refactor(organizations), docs(readme), etc.

## Licensing policy (hard rule)

Every dependency (NuGet and npm) must be free for commercial use under a
permissive OSI license (MIT, Apache-2.0, BSD) — verify the license of the
EXACT version before adding it; several .NET staples re-licensed in 2025-2026.
Explicitly banned: **MediatR, AutoMapper, MassTransit v9+, FluentAssertions
v8+, Moq**. Approved equivalents already in the plan: custom CQRS dispatchers,
hand-written mapping in slices, custom Outbox, xUnit asserts (or Shouldly),
NSubstitute. Respawn: verify its license at add time; fallback is a small
TRUNCATE-based reset helper of our own. If a dependency later changes license,
pin the last free version and open an ADR.

**The rule has two tiers, because "dependency" means two different things.**

- **Tier 1 — what we link or redistribute** (NuGet, npm, **and model weights**): permissive only, no exceptions. This is the rule above. A non-commercial or AGPL model shipped inside our process is exactly as much of a problem as an AGPL library, and the original policy did not cover weights at all.
- **Tier 2 — services we run beside the app** (container images: Postgres, Elasticsearch, Keycloak, Qdrant, Grafana, …): **copyleft is acceptable**. Running stock AGPLv3 software as a separate process over a network boundary imposes no obligation on Tendero; the AGPL trigger is distributing or serving a *modified* version, and SSPL's is offering *that software* as a service. Tendero does neither. Prefer permissive when it is a genuine drop-in (**Valkey**, BSD-3, over Redis), but do not reject a better tool over a licence that never binds.

Record the tier when adding anything, and check the exact tag or revision rather
than the project — **Grafana** went AGPLv3 in April 2021 and **Redis** went
RSALv2/SSPL in March 2024, and neither announcement is visible from the package
name.

## Testing rules

- New slice = unit tests + (if it has a port) contract test inheriting the
  abstract suite. See `CatalogSourceConnectorContractTests`.
- Deterministic fakes over mocks where possible; NSubstitute when not.
- Test data: explicit builders + Bogus. No AutoFixture.
- Any change touching search relevance MUST keep NDCG@10 ≥ the committed
  threshold (`tools/SearchEval`). If a regression is intentional, update the
  golden set in the same PR and explain why in the description.

## Definition of done

Build clean with `-warnaserror` · tests green including architecture tests ·
new decisions recorded as ADR if they constrain the future · CLAUDE.md/docs
updated if conventions changed · user-facing strings exist in es AND en.

## Reference docs (read when relevant, not preloaded)

- **What to do next — the living board: @docs/delivery-plan.md**
- Review of what exists today (2026-09-02): @docs/analysis/current-state.md
- Capability gap analysis: @docs/analysis/gap-analysis.md
- Where each future capability fits architecturally: @docs/analysis/roadmap.md
- Full project plan and phase breakdown: @docs/initial-plan.md
- Architecture deep-dive: @docs/architecture.md
- Testing strategy detail: @docs/testing.md
- Search evaluation / golden set format: @docs/search-evaluation.md
- Design system and copy voice: @design/DESIGN.md
- Brand usage: @brand/BRAND.md
- ADR index: @docs/adr/README.md
- Feature specs in progress: @docs/specs/
- Blog article workflow: @docs/blog-workflow.md
- Blog publication order and drafts: @docs/blog/index.md

## Do NOT

- Do not add cross-slice references "just this once". When a second slice needs
  a port, the port moves to `Ports/`; it never gets referenced where it was born.
- Do not return a strongly-typed id straight from an endpoint. `ProductId` is a
  record struct and serialises as `{"value":"…"}`; the wire wants a flat string,
  and no unit test will catch it.
- Do not bypass the Outbox for side effects.
- Do not add npm/NuGet dependencies without asking — the dependency budget is
  deliberately small, every addition is an architectural decision, and it must
  pass the Licensing policy above (no commercial/dual-license packages, ever).
- Do not "fix" a red architecture test by weakening the rule; fix the code.
- Do not write user-facing strings in a single language or hardcode them in templates.
- Do not use LangChain/AutoGen/Python frameworks in the .NET services; the
  Python sidecar exists only for evaluation and document ingestion.
