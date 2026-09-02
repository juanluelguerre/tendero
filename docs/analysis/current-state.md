# Current state

What Tendero actually is on 2026-09-02, read from the code rather than from the
documentation. Every claim carries the path that supports it; where a claim is an
absence, the absence was checked, not assumed.

This is an inventory, not a report card. It exists so that
[`roadmap.md`](roadmap.md) starts from the repository that exists.

## 1. Scale, and why it changes how you read the rest

| | |
|---|---|
| First commit | 2026-08-29 |
| Last commit reviewed | 2026-09-01 (`1e76878`) |
| Commits | 48 |
| C# | ~3,640 lines across 74 files |
| TypeScript | ~1,140 lines across 28 files |
| Tracked files | 267 |
| NuGet packages | 18 |
| HTTP endpoints | 6, plus `/health` and `/alive` |
| Tests | 68 `[Fact]`/`[Theory]` attributes, **117 executed cases, all passing** |

**Tendero is four days old.** Most of what follows reads as "missing" only
because the documentation is written in the present tense about a system that is
still mostly a plan. That is worth saying once, plainly, because the distance
between the two is the most useful thing this review found — and it is a gap in
the documentation, not a failure in the code.

The code that exists is good. There is not much of it.

## 2. What exists

### Domain

`src/Catalog/Domain/Product.cs` is a real aggregate. `LocalizedText` name, slug
and description; images identified by the SHA-256 of their content
(`src/Catalog/Adapters/FileSystemImageStore.cs`), so re-importing a photo cannot
duplicate it; `ExternalReference(Source, ExternalId)` backed by a unique index
that *is* the import's idempotency key
(`src/Persistence/Configurations/ProductConfiguration.cs`); status
Draft → Active → Archived, with `Publish()` refusing to resurrect an archived
product.

Two details are better than they had to be. `PrimaryImage` — the lowest
`SortOrder` — lives on the aggregate because it is a catalogue rule, after the
backoffice listing and the search document computed it separately and disagreed.
And `Slugify` folds diacritics through an explicit table rather than Unicode
normalisation, because the repo builds with `InvariantGlobalization=true`, where
`Normalize(FormD)` is a silent no-op that returns the string intact.

`src/Ordering/Domain/Order.cs` is also a real aggregate, and its declarative
`AllowedTransitions` table is the single best idea in the codebase: seven states,
twelve edges, readable at a glance, every transition funnelled through one
private `TransitionTo` that raises the matching event. Currency is owned by the
order rather than by each line, which is what lets `Total` exist when no lines
remain.

`src/SharedKernel/SharedKernel.cs` carries strongly-typed ids (record structs
over GUID v7), `Money`, `Culture.Normalize`, `LocalizedText` with its
requested → fallback → first resolution chain, and `AggregateRoot`.

### CQRS, hand-rolled

`src/SharedKernel/Cqrs/` — `ICommand<T>`/`IQuery<T>`, three dispatchers, handler
discovery by assembly scanning, and exactly one pipeline step (FluentValidation).
Reflection is resolved once per request type and cached in a
`ConcurrentDictionary`. It uses `GetTypes()` rather than `GetExportedTypes()`,
which matters: internal handlers were silently skipped until the dispatcher got
its own tests.

### Persistence and the outbox

One `TenderoDbContext`, three schemas (`catalog`, `ordering`, `outbox`). The
outbox is the real thing rather than a queue table with a good name: both
`SaveChanges` overloads drain `AggregateRoot.DomainEvents` into `OutboxMessages`
**inside the same transaction** as the state change that raised them
(`src/Persistence/TenderoDbContext.cs`). The filtered index
`HasFilter("processed_at IS NULL")` keeps the drain query cheap.

`src/Workers/OutboxProcessor.cs` polls every 2s, batches 50, stops retrying a
message at 5 attempts, and records `tendero.outbox.lag` as a histogram — the
metric that says whether eventual consistency is still eventual.

### Search

Two indexes with native analysers, `products_es` (spanish) and `products_en`
(english), created idempotently at worker startup (`src/Search/Elasticsearch.cs`).
The query is two `multi_match` clauses under a `should`: `cross_fields` with
`operator: and` so terms may spread across fields, plus `best_fields` with
`fuzziness: AUTO` for typos, because `cross_fields` does not support fuzziness.
Fields are `name^3, brand^2, attributesText^2, description`.

`ProductIndexProjection.ApplyAsync` in `src/Search/Contracts.cs` holds the
indexing rule once — Active is indexed, everything else removed — and both the
outbox handler and `POST /api/search/reindex` call it. That is why the rebuild
converges instead of merely adding.

### The quality gate

`tools/SearchEval` is the most mature thing in the repository. It drops the
indexes (product ids are fresh GUID v7 per run, so stale documents would compete
in the ranking), indexes the seed through the real ports, refreshes explicitly
(Elasticsearch refreshes about once a second, so otherwise the score depends on
timing), scores both cultures and exits non-zero below threshold.

Both reproducibility fixes came from wrong numbers, and both are recorded in
`docs/search-evaluation.md`: two runs of identical code scored 0.674 then 0.360,
and later 0.860 then 0.769.

Committed baseline, five consecutive runs identical:

| culture | NDCG@10 | recall@50 | threshold |
|---|---:|---:|---|
| es | 0.860 | 0.841 | 0.85 / 0.83 |
| en | 0.720 | 0.682 | 0.71 / 0.67 |

### Frontends

Both apps are real, and both have exactly one route.

`frontend/apps/storefront` — a search page
(`src/app/features/search/search-page.ts`) built as a discriminated union over
signals (`idle | searching | done | failed`), with `aria-live` status regions and
a genuine broken-image fallback that swaps in a labelled tile. The seed points at
`cdn.example.com` on purpose, so that fallback is the default path on a fresh
clone.

`frontend/apps/backoffice` — a review queue
(`src/app/features/review-queue/review-queue-page.ts`) that lists drafts,
publishes one, reloads from the server rather than updating optimistically
(because the outbox means the index lags), shows language completeness as a badge
paired with a word, and offers "import the sample catalog" as its empty state.
Its docblock records that it used to be a placeholder telling you to run curl.

The design-token bridge is a verified three-hop chain from each app's
`styles.css` through `libs/shared/tokens` to `design/tokens.css`, and **no hex
literal appears in any component file**.

### Tests

`tests/Architecture.Tests/ArchitectureRules.cs` — six executable rules. The first
is better than it looks: the forbidden-dependency list is computed by reflection
from each assembly's own references rather than hand-written, with an assertion
that the list came out non-empty, so a clean context cannot pass vacuously.

`tests/Catalog.Tests/ConnectorContractTests.cs` — a real abstract contract suite
(stable lowercase source key, unique non-empty external ids, ISO-639-1 name keys,
three-character ISO-4217 currency, absolute image locations) that
`SeedCatalogConnectorContractTests` inherits in three lines. The seed file is
copied into the test project, so the shipped catalogue is validated by the
contract.

## 3. What is half-built

These are not bugs. They are seams designed for something that has not arrived —
each one a comment promising a caller that does not exist.

| Seam | Where | Waiting on |
|---|---|---|
| `Product.Localize` — "el punto de entrada del slice de enriquecimiento con IA" | `src/Catalog/Domain/Product.cs` | An AI enrichment slice. None exists. |
| `Product.Archive()` | `src/Catalog/Domain/Product.cs` | It has no endpoint, no command and **no caller anywhere in `src/`**. `PublishProduct` answers 409 with "Restore it before publishing" and no restore operation exists. |
| `SearchProductsHandler` — "decidirá por feature flag entre léxico / híbrido" | `src/Search/Features/SearchProducts.cs` | Hybrid search, and a feature-flag mechanism. Neither exists. |
| `ProductUpserted` — "el worker de indexación (Elasticsearch + Qdrant)" | `src/Catalog/Domain/Product.cs` | Qdrant. Not declared, not referenced, not packaged. |
| `Order.Cancel` — "la saga de compensación llama aquí" | `src/Ordering/Domain/Order.cs` | A saga. Nothing calls `Cancel`. |
| `public partial class Program` — "para los tests de integración más adelante" | `src/Api/Program.cs` | Integration tests. There are none. |
| `AllowedTransitions[PaymentFailed]` contains `Pending` | `src/Ordering/Domain/Order.cs` | No public method transitions to `Pending`. The test matrix flags it. |

`src/Ordering` deserves its own line. It is **one file**. Its `.csproj` has zero
package references and one project reference. There is no cart, no checkout, no
`IOrderRepository`, no payment port, no saga, no returns. `Order.Place` is called
from exactly one place in the repository: `tests/Ordering.Tests/OrderBuilder.cs`.
The aggregate is mapped in EF and persisted to a table that nothing ever writes.

`src/Ucp` deserves the next one. It is a directory containing a single 65-byte
`README.md`. There is no `.csproj`, and it is **not in `Tendero.slnx`** — while
`CLAUDE.md` lists `ElGuerre.Tendero.Ucp` among the project's root namespaces as
though it were a project.

## 4. Technical debt, ordered by what it blocks

**1. No authentication, authorization or CORS.** ~~`src/Api/Program.cs` registers
Carter, validation, problem details, and nothing else.
`POST /api/catalog/products/{id}/publish` publishes to the public catalogue and
is open to anyone who can reach the port.~~
**Resolved 2026-09-02.** JWT bearer validation, CORS, three policies, and every
endpoint carrying either a policy or an explicit `AllowAnonymous` — enforced by a
test, so public stays a decision rather than an omission. The issuer is a
development one for now (`src/DevIssuer`), which publishes a real discovery
document and JWKS; the validation is production code and does not change when
Keycloak replaces it. Verified live: no token 401, wrong role 403, right role
passes, public search unaffected.

**2. No CI.** ~~There is no `.github/` directory and not a single `.yml` file in
the repository. The NDCG gate, the six architecture rules and the contract suite
all exist and all run only when someone remembers.~~
**Resolved 2026-09-02**, the first task off this review: `.github/workflows/ci.yml`
runs the build with `-warnaserror`, the 117 tests, the six architecture rules,
the Nx module boundaries, and the NDCG gate against an Elasticsearch service
container — posting the report to the job summary and to the pull request. The
finding stays here rather than being deleted, because it is what the badge on the
README was claiming for four days.

**3. No migrations.** ~~ADR 0008 says so explicitly, and
`src/Workers/DevelopmentSchemaInitializer.cs` calls `EnsureCreatedAsync` in
Development only. The failure mode is quiet: `EnsureCreated` does nothing when the
schema already exists, so the first additive model change leaves every existing
developer database silently wrong, with no error.~~
**Resolved 2026-09-02.** An initial migration, `SchemaMigrator` in place of
`EnsureCreatedAsync`, and an integration test that creates one database from the
model and another from the migrations and compares the two schemas column by
column and index by index — which is what makes the baseline trustworthy rather
than merely generated.

**4. No OpenAPI.** ~~`frontend/libs/shared/api/src/lib/*.ts` hand-mirrors the .NET
DTOs, and its own docblocks flag the risk. There is no contract test on either
side, so drift is silent and surfaces at runtime.~~
**Resolved 2026-09-02.** `docs/openapi/tendero.json` is committed, a contract
test asserts it is what the API serves, and the frontend types are generated
from it. Getting there required moving the endpoints to `TypedResults`: with
`Results.Ok(...)` the generator could infer nothing and the document had no
response schemas at all.

**5. No integration tests.** ~~No `WebApplicationFactory`, no Testcontainers, no
`Category` trait. Nothing exercises Postgres, the jsonb converters, the outbox
drain, the unique indexes, the Carter wiring or the Elasticsearch adapter against
a real dependency.~~
**Resolved 2026-09-02.** `tests/Integration.Tests` covers import → outbox →
drain → projection against a real Postgres, plus the migration baseline;
`tests/Api.Tests` boots the API. `dotnet test --filter Category=Integration` —
documented in `CLAUDE.md` and matching zero tests until now — matches four.
The Elasticsearch adapter is still only exercised by the SearchEval gate.

**6. The two real frontend pages are untested.** `search-page.ts` and
`review-queue-page.ts` hold all the logic — state machines, broken-image
fallback, publish-and-reload, per-row disabled state — and neither has a test.
The four frontend test cases assert that a `router-outlet` renders and that the
culture list is `['es','en']`.

**7. `docs/specs/` contains only `TEMPLATE.md`.** Zero specs for six shipped
endpoints, while `initial-plan.md` §8 and the README both advertise one spec per
feature.

**8. Four translated i18n keys are dead.** `nav.orders`, `nav.agents`,
`nav.search` and `product.addToCart` exist in both languages and are referenced
**zero** times. They are the product gap drawn by the frontend itself.

**9. Telemetry source names are inconsistent.** `TelemetrySources` exists to hold
them; `ListProductsHandler`, `PublishProductHandler`,
`ElasticsearchLexicalSearch` and `ReindexProducts` use string literals instead.
Same values today, so nothing is broken — and nothing would tell you when it
breaks.

## 5. The gap between the documentation and the code

Recorded here rather than fixed in place: each doc gets corrected by the PR that
implements the thing it describes.

| Claimed | Where | Reality |
|---|---|---|
| "NDCG@10 — tracked in CI" badge, four prose mentions, a *draft-ready* article | `README.md`, `docs/testing.md`, `docs/blog/02-*.md` | No CI exists |
| NSubstitute, Bogus, CsCheck, Verify, Testcontainers, Respawn | `docs/testing.md`, README | Testcontainers landed with the migrations (phase 0) and CsCheck with the promotion combination rules (phase 3). Respawn was declined — a fresh database per test is cheaper than a reset. NSubstitute, Bogus and Verify are still prescribed and still absent. |
| `dotnet test --filter Category=Integration` spins up containers | `CLAUDE.md`, README | Matches **zero** tests; the trait does not exist |
| Snapshot of `ProductSearchDocument` per culture, "a public contract with Elasticsearch" | `docs/testing.md` | No snapshot test of any kind |
| `IPaymentProvider`, three adapters behind a port — present tense | ADR 0003, README | The port does not exist |
| "The Python sidecar exists only for evaluation and document ingestion" | `CLAUDE.md`, ADR 0005 | No `.py` file in the repository |
| `PaymentProviderContractTests` | `docs/testing.md` | Does not exist |
| One spec per feature in `docs/specs/` | `initial-plan.md` §8, README | Only `TEMPLATE.md` |
| AI layers are "feature-flagged" | ADR 0004 | No feature-flag mechanism anywhere |
| `release.projects: ["api"]`, and a debug config for `nx serve api` | `frontend/nx.json`, `frontend/.vscode/launch.json` | No `api` project in the Nx workspace |
| Golden set of 50–100 queries per culture | `initial-plan.md` §6 | 22 per culture, against 6 products |
| `ElGuerre.Tendero.Ucp` listed as a root namespace | `CLAUDE.md` | `src/Ucp/` has no `.csproj` |
| Boundary rule "verified by mutation" | `frontend/README.md` | No mutation tooling |
| A `core/` folder per app | `frontend/README.md` | Neither app has one |

Three internal inconsistencies will confuse a reader before they confuse a
maintainer: article 07 places Qdrant/Ollama/Redis in phase 3 while the README and
`architecture.md` place embeddings in phase 2; article 08 says the API has five
endpoints and `architecture.md` says six; the README says seven articles are
drafted and `docs/blog/index.md` shows eight plus one half-drafted.

## 6. The licensing policy was too blunt, and had a hole

`CLAUDE.md` carried one rule: every dependency must be permissive OSI (MIT,
Apache-2.0, BSD). Reviewing it against what the roadmap actually needs surfaced
two problems in opposite directions.

**It was too strict in one place.** `docs/initial-plan.md` §4 names **Grafana**
(AGPLv3 since April 2021) and **Redis** (RSALv2/SSPL since March 2024), and the
blanket rule rejected both. But neither licence binds here: AGPLv3's obligation
triggers on distributing or serving a *modified* version, and SSPL's on offering
*that software* as a service. Running a stock container beside the application,
over a network boundary, does neither. The rule was conflating **what we link**
with **what we run**.

The policy is now two-tiered (see `CLAUDE.md`): permissive only for anything
linked or redistributed, copyleft acceptable for services run alongside.
**Valkey** (BSD-3, the Linux Foundation fork of Redis 7.2.4) is still preferred
over Redis because it is a genuine drop-in, but that is a preference now rather
than a prohibition.

**It was too narrow everywhere else.** The rule covered NuGet and npm. It did not
cover **model weights**, which become first-class dependencies the moment the AI
layer lands — an embedding model, a reranker, a vision encoder. Weights are
linked into our process, so they sit firmly in the strict tier, and a
non-commercial model would have passed every check this repository performed.
That is the gap that mattered.

## What this review did not check

Runtime behaviour. Nothing here was produced by starting the stack; it is read
from source, configuration and committed test data. Claims about what the system
does under load, and the outbox and reindex figures quoted from
`docs/blog/notebook.md`, are inherited from that record rather than re-measured.
