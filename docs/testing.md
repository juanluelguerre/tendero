# Testing strategy

Reference doc — imported on demand from CLAUDE.md. Keep CLAUDE.md itself short.

**What is in the repository today** (2026-09-08): xUnit v3, CsCheck,
NetArchTest.Rules, Testcontainers.PostgreSql, `WebApplicationFactory`, vitest
and Playwright — 547 backend tests, 37 frontend, 20 browser specs. NSubstitute,
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
- Qdrant joins the fixture when hybrid search lands.

## Architecture tests (NetArchTest.Rules)

Executable rules, one test each:

1. `*.Domain` assemblies depend only on SharedKernel.
2. No type in `Features/X` references `Features/Y` (slice isolation).
3. Connector adapters are internal; only ports are public.
4. Nothing outside `Search.Elasticsearch` references `Elastic.Clients.*`.
5. Domain types never reference `Microsoft.EntityFrameworkCore`.

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

1. **Assert the browser, not the arithmetic.** That a refund is 72.55 € is
   `CheckoutLoopTests`'s job. The spec asserts that an amount appears.
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
