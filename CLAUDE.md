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

## Commands

```bash
dotnet run --project src/AppHost              # full stack via Aspire
dotnet build -warnaserror                     # warnings are errors, always
dotnet test                                   # unit + contract + architecture
dotnet test --filter Category=Integration     # Testcontainers (needs Docker)
dotnet run --project tools/SearchEval         # golden set NDCG report
npx nx serve storefront|backoffice            # from frontend/
```

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
   gracefully; never make BM25 depend on Ollama/Qdrant being up.
9. **AI layering**: Microsoft.Extensions.AI for pipelines, Microsoft Agent
   Framework only for real agents (phase 3 shopping assistant), no Agent
   Harness (our agents are short-lived and constrained).

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

- Full project plan and phase breakdown: @docs/initial-plan.md
- Architecture deep-dive: @docs/architecture.md
- Testing strategy detail: @docs/testing.md
- Search evaluation / golden set format: @docs/search-evaluation.md
- Design system and copy voice: @design/DESIGN.md
- Brand usage: @brand/BRAND.md
- ADR index: @docs/adr/README.md
- Feature specs in progress: @docs/specs/
- Blog article workflow: @docs/blog-workflow.md

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
