# Testing strategy

Reference doc — imported on demand from CLAUDE.md. Keep CLAUDE.md itself short.

**What is in the repository today** (2026-09-15, after P8-11): xUnit v3, CsCheck,
NetArchTest.Rules, Testcontainers.PostgreSql, `WebApplicationFactory`, vitest
and Playwright — 950 backend tests (929 green, 21 skipped with a reason), 65
frontend, 20 browser specs. NSubstitute,
Bogus and Verify are still prescribed below and still absent: the deterministic
fakes and the contract suites have not needed a mock, and nothing snapshots
yet. Respawn was declined — a fresh database per test is cheaper than a reset.
Each enters with the first test that needs it (ADR 0006).

## The pyramid, adapted to this project

| Layer | Framework | Speed | When it runs |
|---|---|---|---|
| Unit (domain, slices) | xUnit v3 (NSubstitute when a fake will not do — not needed yet) | ms | every build |
| Property-based | CsCheck | ms | every build |
| Contract (ports) | xUnit abstract base classes | ms–s | every build |
| Snapshot / golden | Verify (planned for the UCP manifest) | ms | every build |
| Architecture | NetArchTest.Rules | ms | every build |
| Integration | Testcontainers, a database per test | s | `--filter Category=Integration` |
| Search relevance | tools/SearchEval (NDCG@10) | s | CI gate on every PR |
| End-to-end | Playwright | s | its own CI job, after the rest |

## Unit tests

- Arrange with **explicit builders** (`ProductBuilder.Default().WithCulture("en")...`).
  Builders live next to the tests of their context. **Bogus** generates realistic
  volume data (names, brands) inside builders — never inline `faker` calls in tests.
- **No AutoFixture**: anonymous data hides which fields matter to the behavior
  under test. If a value is irrelevant, the builder's default says so explicitly.
- Assertions: plain xUnit `Assert` (or Shouldly if it earns its place). Never
  FluentAssertions v8+ (commercial license).
- Mock only at ports, with **NSubstitute** (never Moq). Prefer deterministic in-memory fakes
  for anything with behavior (e.g. `InMemoryProductRepository` used across tests).

## A test that passed first time has proved nothing yet

A test written for code that already exists passes the first time it runs, and
that is exactly the moment it has shown nothing: it might assert the wrong
thing, or nothing at all. So a test added to cover existing code **earns its
place by failing on purpose** — the production code is mutated one rule at a
time (a guard inverted, a filter dropped, a fallback changed, a culture fixed),
the test class is run against each mutation, and every one of them has to turn
at least one test red. Then the file is restored byte for byte.

A mutation nobody notices is a finding, not a formality: either a test is
missing, or the code carries a branch nothing needs.

Mutation testing runs at two levels, decided on 2026-09-16, and they answer
different questions.

### Level 1 — by hand, for every test written for existing code

The question: *does this test catch what it was written for?* A small script and
a list of mutations written per class — the list goes in the commit message that
adds the tests. Fast, intentional, and blind to whatever nobody thought to mutate.
The first sweep, on 2026-09-10, put 80 mutations through five classes that had no
tests of their own (the principal accessor, culture negotiation, the payment
webhook handler, the search document and the outbox worker) and none survived.
Two of those tests found real defects on their first run rather than on a
mutation — which is the other reason to write them.

### Level 2 — Stryker.NET, at every phase close

The question: *what did nobody think to mutate?* `dotnet-stryker` 5.0.0 is in the
tool manifest (ADR 0006), and a phase is not closed until it has run over every
project the phase touched:

```bash
dotnet tool restore
cd src/<Project>
dotnet tool run dotnet-stryker --config-file stryker-config.json --skip-version-check
```

1. **One `stryker-config.json` per mutated project, committed next to it**, naming every test project that exercises it — a project mutated against half its tests reports false survivors (see below). Always `test-runner: mtp`, `concurrency: 1`, and `ignore-methods` for logging and telemetry calls. `src/Search/stryker-config.json` is the template.
2. **The score that counts is `of covered`**: killed ÷ (killed + survived), the quality of the tests that do run the code. `of total` also counts code no unit test reaches, which here is mostly adapters the integration tests prove against real containers; it is reported, not gated.
3. **A ratchet, not a magic number**: a project's `of covered` may not fall below its baseline below. A first run sets the baseline; a higher run raises it.
4. **Every survivor is classified**, in the phase's notes: a real gap, closed with a test (re-proven at level 1) or written down with why it waits; an equivalent mutant nothing can kill; or noise (a message's wording, a configuration string the relevance gate or the integration tests already hold). The percentage is never the goal: chasing it means testing log text and duplicating integration tests with fakes.
5. **Never per pull request** while its Microsoft.Testing.Platform runner is labelled preview — it takes minutes per project, and 5.0.0 under-reports kills above concurrency 1 ([#3832](https://github.com/stryker-mutator/stryker-net/issues/3832)). Revisit when the runner leaves preview, and when the SDK reaches .NET 11 RC1, whose C# 15 its embedded Roslyn 5.9 does not know.
6. The HTML report goes to `docs/blog/reports/` (private), and the phase's row in the delivery plan carries the score.

**Baselines**

| project | mutated with | of covered | of total | set |
|---|---|---:|---:|---|
| `src/Search` | `Search.Tests`, `SearchEval.Tests` | 75.97 % | 48.84 % | 2026-09-16 |

**How it got here.** Evaluated on 2026-09-15 without installing it: Apache-2.0 at
its tag, the hosted dashboard never contacted unless asked, one outbound version
check that `--skip-version-check` removes, and no maintained alternative
(Faultify is GPL-3.0 and last released in 2021). A first trial over `src/Search`
with `Search.Tests` alone gave 983 mutants in six minutes, `of covered` 55.81 % —
and **two runs identical in every unchanged file**, which is what made the preview
runner trustworthy at concurrency 1. Its survivors WITH coverage held six real gaps
the hand-made lists had missed (a page skip nothing checked, tie order after
reranking, an empty-text fallback, two `>=` boundaries, a total branch). Sorting
all 171 survivors found about 80 that were logging, telemetry and configuration
strings, and 37 that were **false**: evaluation code whose tests live in
`SearchEval.Tests`, which the trial had not run. After 17 new test cases and two
strengthened assertions, a third run with both test projects and logging ignored
gave `of covered` **75.97 %**, survivors 93 — the baseline above.

## Property-based (CsCheck)

Value objects carry the invariants worth generating inputs for:

- `Money`: sum is associative, currency mismatch always throws, `a + Zero == a`.
- `LocalizedText`: `In(c)` never throws for any culture string; normalization
  is idempotent; `With` never loses existing translations.
- `Order.AllowedTransitions`: from any status, only listed targets succeed and
  every non-listed target throws (walk the full matrix).

## Contract tests

Every port ships an abstract xUnit suite; every adapter inherits it:

- `CatalogSourceConnectorContractTests` → Seed, Shopify, Medusa...
- `PaymentProviderContractTests` → Fake today; stripe-mock and Stripe test when
  they land (the last one `[Trait("Category","External")]`, excluded from CI).
- `IdentityProviderContractTests` → the development issuer, and Keycloak — which
  did not pass unchanged, and that was the finding (`P7-7`).

A new adapter without its contract-test subclass does not merge.

## Snapshot tests (Verify)

- `ProductSearchDocument.FromProduct(...)` per culture — the index shape is a
  public contract with Elasticsearch; changes must be visible in the diff.
- Rendered LLM prompts (when enrichment lands): the prompt IS the behavior;
  snapshot it so prompt drift shows up in code review.

## Integration tests (Testcontainers)

- One fixture boots Postgres once per test run and hands every test **a fresh
  database** — a `CREATE DATABASE` on a warm container, which is cheaper than a
  reset and stops the order of the tests from mattering. Respawn was declined
  for that reason. Tests skip with a reason when Docker is absent.
- **`tests/Persistence.Tests` needs no container at all**, which is the reason
  it is a project of its own rather than a class in here. Building the model is
  pure reflection over the configurations, so it runs on a machine with no
  Docker and in the first seconds of CI. It exists because seven EF member names
  went stale in a rename and the only tests that could see it were these ones —
  a defect is not caught by the suite that cannot run.
- The API is tested through `WebApplicationFactory` **without** containers
  (`tests/Api.Tests`): neither the DbContext nor the Elastic client connects on
  construction, so the OpenAPI document, the endpoint policies, the handler
  composition and the development issuer are checked in process.
- Indexing tests: import seed sample → drain outbox → assert documents exist in
  `products_es` and `products_en` with the right analyzer behavior
  (e.g. "zapatilla" matches "zapatillas").
- Qdrant has its own container (`QdrantVectorLayerTests`, `qdrant/qdrant:v1.18.0`)
  since P8-4, and the refusals of a mixed vector space are proven there.
- **A suite that composes the worker's container must register what the worker
  registers**, not only what it asserts on. `OutboxDrainTests` scanned the Search
  assembly for handlers and never registered the vector layer, so after P8-4
  every `ProductUpserted` failed in the worker with every other suite green.
- **The model server is opt-in**, and the reason is a measurement: on a CPU the
  embeddings container downloads 2.2 GB and holds 3.7 GiB of RAM sized to the catalogue — 12.6 GiB with TEI's defaults (ADR 0031),
  more than a CI runner has to spare. `EmbeddingModelServerTests` runs only with
  `TENDERO_MODEL_TESTS=1` and skips with that reason otherwise. What can be
  proven without the model — the request the SDK sends, the descriptor, the
  registration — is proven in `tests/Search.Tests` on every build.

## Architecture tests (NetArchTest.Rules)

Executable rules, one test each:

1. `*.Domain` assemblies depend only on SharedKernel.
2. No type in `Features/X` references `Features/Y` (slice isolation).
3. Connector adapters are internal; only ports are public.
4. Nothing outside `Search.Elasticsearch` references `Elastic.Clients.*`.
5. Domain types never reference `Microsoft.EntityFrameworkCore`.
6. A **transport** — `Mcp` today, `Ucp` from P9-5 — implements no handler and
   reaches no `*.Domain` namespace. It is the rule that keeps a second protocol
   from quietly becoming a second system: a tool is six lines precisely because
   the answer already exists in a slice, and a handler declared beside the tool
   would be the same question answered twice with the validator and the audit
   decorator on only one of them.

The list is not the count. Sixteen tests run these five ideas across every
assembly, because most of them are a loop over the contexts rather than one
assertion.

When a rule blocks you, the answer is a design conversation (maybe an ADR),
never weakening the rule inline.

## Search relevance as a test suite

- Golden set: `tools/SearchEval/golden/{culture}.json` — query, annotated
  product ids with relevance 0–3, and a comment explaining WHY (the comment is
  what makes annotations reviewable).
- CI computes NDCG@10 and recall@50 against a freshly indexed seed catalog and
  fails under the committed thresholds in `eval.thresholds.json`.
- Changing thresholds or annotations requires justification in the PR body.

## End-to-end, and how little of it there should be

Playwright is the **thinnest** layer here, and deliberately so. Below it there
are already 503 tests, five of which drive the whole commerce loop against a
real Postgres and the real outbox. Repeating any of that in a browser buys a
slower copy of a test that exists.

**What only a browser can prove** is the wiring between Angular and the API: a
route guard, a token interceptor, a table that derives its buttons from a status
the server sent, and an HTTP status rendered as a sentence a shopper can act on.
That is the whole brief.

Three rules keep it honest:

1. **Assert the browser, not the arithmetic.** What a refund comes to is
   `ReturnRequestTests`'s job, and that a total is what its lines show plus the
   shipping is `QuoteCartHandlerTests`'. The spec asserts that an amount
   appears. The rule is right and it has a cost worth remembering: on
   2026-09-11 a person with a pencil noticed that a cart's lines added up to
   78,33 € and its total said 94,78 € (ADR 0028), because the invariant a
   browser spec was told not to check had been checked by nobody at all.
2. **Web-first assertions, never a sleep.** Half of this system is asynchronous
   by design — an order ships and the capture arrives through the outbox — so
   `expect(...).toPass()` and auto-retrying locators are the only correct tool.
   A `waitForTimeout` here is a flake with a timer on it.
3. **Sign in through the screen every time.** A cached `storageState` looks like
   the obvious optimisation and does not work: the development issuer mints its
   signing key per process, so a saved token dies with the next restart.

### Writing them with an agent, running them in CI

The two are different jobs and only one of them produces an artifact.

**Claude Code with the Playwright MCP server is for authoring and diagnosis.**
It reads the accessibility tree, so the locators it proposes are role-and-name
rather than brittle CSS, and it can replay a failing trace and say what changed.
That is genuinely faster than writing selectors by hand.

**The committed spec is the gate.** A check that only exists when somebody asks
an agent to look is the badge this repository already wrote an article about —
"NDCG tracked in CI" on a repo with no CI. Whatever the agent produces goes
through a pull request like any other code.

**No self-healing.** The Playwright Healer agent replays a failure, finds an
"equivalent" element and patches until the test passes. In a shop whose whole
argument is that a system saying *no* is the interesting part — a suppressed
discount, a declined card, a denied mandate — a tool designed to turn a refusal
into a pass is pointed the wrong way. Use the agent to explain a failure; let a
person write the fix.

## Naming

`MethodOrBehavior_Condition_Expectation` is too rigid for domain tests; prefer
sentences: `Cancelling_a_delivered_order_is_rejected()`. Readability wins.
