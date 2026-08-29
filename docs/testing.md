# Testing strategy

Reference doc — imported on demand from CLAUDE.md. Keep CLAUDE.md itself short.

## The pyramid, adapted to this project

| Layer | Framework | Speed | When it runs |
|---|---|---|---|
| Unit (domain, slices) | xUnit v3 + NSubstitute | ms | every build |
| Property-based | CsCheck | ms | every build |
| Contract (ports) | xUnit abstract base classes | ms–s | every build |
| Snapshot / golden | Verify | ms | every build |
| Architecture | NetArchTest.Rules | ms | every build |
| Integration | Testcontainers + Respawn | s | `--filter Category=Integration` |
| Search relevance | tools/SearchEval (NDCG@10) | s | CI gate on every PR |

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
- `PaymentProviderContractTests` → Fake, stripe-mock, Stripe test (last one
  `[Trait("Category","External")]`, excluded from CI).

A new adapter without its contract-test subclass does not merge.

## Snapshot tests (Verify)

- `ProductSearchDocument.FromProduct(...)` per culture — the index shape is a
  public contract with Elasticsearch; changes must be visible in the diff.
- Rendered LLM prompts (when enrichment lands): the prompt IS the behavior;
  snapshot it so prompt drift shows up in code review.

## Integration tests (Testcontainers)

- One collection fixture boots Postgres + Elasticsearch once per test run;
  **Respawn** (verify license at add time; fallback: our own TRUNCATE helper) resets Postgres between tests (faster than re-creating containers).
- API tested through `WebApplicationFactory` wired to the containers.
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

## Naming

`MethodOrBehavior_Condition_Expectation` is too rigid for domain tests; prefer
sentences: `Cancelling_a_delivered_order_is_rejected()`. Readability wins.
