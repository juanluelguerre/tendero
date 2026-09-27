# Testing strategy

Reference doc — imported on demand from CLAUDE.md. Keep CLAUDE.md itself short.

**What is in the repository today** (2026-09-17, after layer B moved to the
evaluation tool): xUnit v3, CsCheck, NetArchTest.Rules, Testcontainers.PostgreSql,
`WebApplicationFactory`, vitest and Playwright — 1035 backend tests (1014 green,
21 skipped with a reason), 89 frontend, 20 browser specs. Layer B's 37 cases went
from `Search.Tests` to `SearchEval.Tests` with the code they drive, and 13 more
went with the production wiring that no longer exists. NSubstitute,
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
| Design system | vitest over `design/tokens.css` and `design/` | ms | every build |
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
| `src/Search` | `Search.Tests`, `SearchEval.Tests` | 82.07 % | 57.06 % | 2026-09-26 · re-derived after layer B left |
| `src/Ucp` | `Ucp.Tests`, `Api.Tests` | 70.95 % | 62.50 % | 2026-09-26 |
| `tools/TrafficSim` | `TrafficSim.Tests` | 87.66 % | 87.10 % | 2026-09-26 · `Model/`, `Scheduling/`, `Queries/` |
| `src/Knowledge` | `Knowledge.Tests` | 72.33 % | 57.91 % | 2026-09-27 · first run 49.76 % |

A project the phase touched in a few lines only is run **scoped to those lines**
(`-m 'File.cs{a..b}'`) and sets no baseline: a score over twenty mutants is not
the project's. **The span is in characters, not lines** — the first attempt, in
line numbers, matched nothing and Stryker reported "unable to calculate a mutation
score" rather than an error; convert first (the offsets start at the file's first
character, after any byte-order mark). **Stryker only mutates a project a test
project references directly**: `src/Mcp` and `src/ServiceDefaults`, reached from
`Api.Tests` only through `src/Api`, fail with *No project found* and have no
config; their phase code is proven by hand mutations at level 1.

**Search's baseline was measured with layer B inside `src/Search`, and layer B
left the same day** (ADR 0032, amended): the classifier, its anchors, its
protocol and `ScopeSet` are in `tools/SearchEval/QueryScope` now, and
`SearchAnswerer` lost a dependency with them. The mutants that produced 76.23 %
are therefore no longer the mutants of this project, so **the next phase close
re-derives it rather than ratcheting against it** — a first run after the move
sets the number again, the way the first run of a project always does. The
survivors below are kept as they were classified, minus layer B's, because they
are still the notes for the code that stayed.

**What a survivor may be that the runner makes it.** A mutant inside a `static
readonly` field or a static property initializer — a phrase list, an analyser
name, an index's field list — is reported *Survived* even when a test kills it:
the initializer runs once per test process, before the mutant is switched on, so
the MTP runner never sees it. Four of them were killed by hand on 2026-09-17 (a
help topic's phrase, its destination, the full-match fields, the vector layer's
not-configured status). Classify these as runner artefacts, prove one by hand
when a new kind appears, and do not rewrite production code into instance
members to please a preview runner. **A literal inside a lambda stored in a
static field is not one of them**: the lambda runs on every call, so its mutant
is live, and a survivor there is a real survivor (the `Active` filter below).

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
gave `of covered` **75.97 %**, survivors 93 — the Search baseline until 2026-09-17.

**2026-09-17 — the phase close of the search answers (ADR 0032).** Two projects
touched, both run. Reports in `docs/blog/reports/2026-09-17-no-answer-research/stryker/`.

- **`src/Search`, first run: `of covered` 70.75 %**, under the 75.97 % baseline
  (1251 mutants, 160 survivors). The ratchet did its job: the new code had arrived
  with holes. Two groups were real and are closed, 30 mutations for 30 kills at
  level 1: the report's new shares and the threshold rule in `CultureScore`
  (`Admitted` and `NoticeRate` could be multiplied instead of divided, a threshold
  met by either metric instead of both — `CultureScoreTests`, a `JudgedAt10`
  assertion), and every line of `AddHybridSearch`/`AddSearchStrategies`, which the
  API's and the worker's composition tests see from projects Stryker does not run
  (`HybridSearchCompositionTests`, one more `VectorLayerRegistrationTests` case);
  plus `QueryScopeProtocol.Accepts` with one side empty. **Second run: 76.23 %**
  (130 survivors, 216 without coverage), the new baseline; identical to the first
  in every mutant the new tests did not target.
- **The 130 survivors, classified** (the score stands as measured in run 2;
  Stryker was not re-run after the fix below). 46 **runner artefacts** in static
  initializers (above). 1 **real gap, closed after run 2**: the lexical `Active`
  filter (`"active"` → `""` in `Elasticsearch.cs`), first misread as an artefact —
  the literal lives in the lambda body, and `ElasticsearchRequestBodyTests`
  asserted the key `status` but never its value; the value is asserted now, and
  the hand mutation turns four of its tests red. 19 **equivalent**: accent folding
  for letters no help
  phrase depends on (`í` proven by hand — every phrase carrying *política* also
  carries *devolución*), an empty-word filter, hybrid's `total` branches
  (`ranked.Count ≥ Hits.Count ≥ Total` always), a card lookup with no ids, RRF's
  best-position `Min`/`Max` and its tie order (equal fused scores imply equal
  positions at the depths the shop uses), `Any`/`All` over the one golden source,
  a retry flag's initial
  value, the empty-golden-set guard. 14 **argument guards** another guard one call
  down already throws for, 10 **message wordings** and 11 **logging, timing and
  telemetry** calls: noise. 12 **adapter plumbing** (HTTP clients, base
  addresses, `EnsureSuccessStatusCode`, the Qdrant registration) that the
  container and model-server tests prove against the real thing. **Written down,
  waiting**: 3 in the candidate reranker's revision check (an evaluation-only path
  from 2026-09-16).
- **Fourteen of those 130 left `src/Search` later the same day, with layer B.**
  Eleven were the classifier's own — `AnchorQueryScope`'s normalisation, its `Max`
  over the inside anchors, the culture filter, the English fingerprint fields, the
  anchors file's bytes, its `TimeProvider` — and three sat in the equivalent group
  above and have been taken out of it: `AddQueryScope` while no calibration is
  embedded, the fingerprint's sort direction, and the tolerance boundary with no
  committed calibration. That is why equivalent reads 19 rather than the 22 the
  run reported, and why the classified groups now sum to **116** rather than 130:
  `46 + 1 + 19 + 14 + 10 + 11 + 12 + 3`. The run's own report still says 130 —
  it measured a project that no longer exists in that shape, which is the other
  reason the next phase close re-derives the baseline rather than ratcheting.
  Worth keeping as the lesson: every one of the fourteen was unreachable from any
  shipped call, and **a test cannot kill a mutant in code nothing runs** — a
  ratchet was never going to close them.
- **`Ucp.Tests` stays out of Search's config**: the only Search code it reaches is
  `HelpTopicDestinations`, a static dictionary whose mutants are runner artefacts
  `HelpTopicsTests` already kills by hand.
- **`src/Ucp`, first baseline.** Mutated with `Ucp.Tests` and `Api.Tests`, which
  both exercise it; `Integration.Tests` also does (every product against a real
  Postgres) and is left out, because a container per mutant is not a phase-close
  cost. First run 58.39 %; the survivors in this phase's own code were real — the
  notices could all have been written in Spanish, lost their `type` and `path`, or
  been dropped from a page with products — and are closed (`UcpSearchNoticesTests`
  strengthened, three `UcpCatalogConformanceTests`), 15 mutations for 15 kills.
  **Second run: 69.34 %**, 42 survivors, the baseline. One is noise (the
  no-results wording); the other 41 are in phase 9's code and **written down**:
  the mapper's optional fields (tags, media, options, title fallback — the schema
  accepts both shapes, and `UcpCatalogIntegrationTests` validates the real
  catalogue outside Stryker), the search operation's defaults, category filter,
  paging arithmetic and cursor parsing and the lookup's 50-identifier boundary
  (verified live on the Aspire stack in `P9-12`, unit-tested by nothing), the REST
  binding's empty-`id` refusal, its request default and its base URL, the lookup's
  `not_found` messages and `featured` correlation, and the agent metadata's
  profile check. They are the next
  Ucp work's first tests, not this phase's.

**2026-09-27 — the phase close of *Knowledge* (phase 10).** One new project in
full, three touched ones scoped to what the phase wrote, and the gates. Reports in
`docs/blog/reports/2026-09-27-knowledge/stryker/`.

- **`src/Knowledge`, first baseline.** First run **49.76 %**, 211 survivors — a
  project written test-first where it mattered and read by the golden set
  everywhere else, and the golden set is not a unit test. Classified, the real
  gaps were: typing case by case (the yes/no words, an option whatever its case, a
  text trimmed, an unknown attribute refused, a claim that types nothing not
  being a refusal); an *"English"* statement written in Spanish; the chat
  extractor's **JSON schema**, of which only the attribute list was checked —
  the schema is part of the prompt, and it is now pinned whole beside
  `PromptVersion`, so changing one without the other is a red test; the reader
  turning a model's `null` into `""`; which texts the model reads and which
  statement the scorer hears; a model registered without its scorer not being
  used; a claim resting on both languages falling when either changes; every
  handler **saving** (`InMemoryKnowledge` now counts saves — a decision never
  saved is a decision that did not happen); the three metrics' labels; the specs
  table's row order, its better-supported claim, the page-language fallback and
  the validator's boundary; a failure reason cut to its column. **Final run:
  72.33 %**, 119 survivors, the baseline. They are **runner artefacts** (63:
  the rules' material and count tables, `StatementLanguage`'s function words and
  the metric names and descriptions — static initializers; one proved by hand:
  dropping *"lino"* turns the new vocabulary theory red), **guards** (19:
  `ThrowIfNull` whose removal only moves the failure to a null reference),
  **exception messages** (9), **equivalent** (14: EF's default `""` initializers,
  `<` against `<=` at a boundary that returns the same string, `First` against
  `FirstOrDefault` over a non-empty group, `LocalizedText`'s fallback making
  `In("en")` and `In("")` the same answer, the schema's `"claims"` name, a JSON
  null read either way, `Count == 0` against `<= 0`), and **the model's
  instructions** (the prompt text, 14): unobservable without a model, and measured
  where they matter, by the `claims` gate. `OpenAICompatibleChat`'s transport and
  `InProcessOnnxChat` are adapters with no unit tests by design; the gate and the
  live run prove them.
- **Scoped runs.** `src/Catalog` (`AttributeDefinition.Accept`,
  `AttributeValue.Key`, the mapper's use of `Accept`, the `SetProductAttribute`
  slice): first run 67.86 % — `Key` and `HeldAs` had **no test at all**, and the
  yes/no words, the import's fallback and the audit of an unchanged value were
  unasserted; three runs later **97.83 %**, one survivor, a guard. `src/SharedKernel`
  (`AddTenderoSlices`): it had no unit test — only the integration test composed
  it — and got `SliceRegistrationTests`; **100 %**. `tools/SearchEval`
  (`KnowledgeGates`, first config for the tool): 66.67 %, the survivors every one
  of them the words a red CI run prints; pinned, output included; **100 %**.
- **By hand, where Stryker cannot reach.** `ApplyApprovedClaimToCatalog` in the
  worker (returning before it dispatches turns the end-to-end test red), the
  specs' `AppliedAs` match, the culture filter and the gate's verbatim floor —
  each cited in its commit and each killed. Three of them were run minutes *after* the commit that cited them rather than before; all three held, and the habit is the other order.

**2026-09-26 — the phase close of *Seeing it run* (phase 9b).** Every project
the phase touched, in about an hour of machine time: three full runs, five
scoped, two by hand. Reports in `docs/blog/reports/2026-09-26-seeing-it-run/stryker/`.

- **`tools/TrafficSim`, first baseline.** First run **58.44 %** (64 survivors),
  6.5 minutes — most of it the Monte Carlo test, 200 000 walks per persona, paid
  once per mutant the chains cover. The survivors were one real gap:
  `ShopModelTests` checks what the chains add up to, and nothing checked what a
  journey draws on top of them — language, consent, the card and its retry, the
  reasons, think time, one query per search, the step limit, SplitMix64, the
  arrival index, the compressed day. The published rates' tolerances hid all of
  it. `JourneyDrawTests` checks each on a persona built for it (a chain straight
  to Pay measures the card in every walk), pinned to SplitMix64's reference
  outputs; 17 hand mutations for 17 kills. The walks went from 200 000 to 50 000
  — twice what the tolerances need, the rates moving from 6.36 / 71.54 / 1.73 %
  to 6.29 / 71.86 / 1.71 %. **Second run: 87.66 %** (19 survivors), 4.4
  minutes, the baseline. Three of the 19 were gaps too small to see before — a
  one-step persona cannot tell `Any` from `All`, and the retry's own decline
  rate was unchecked — closed after run 2, three mutations for three kills. The
  other 16 are **equivalent**: `<` against `<=` on a uniform double (9), `>>>`
  against `>>` on a `ulong` (5), shares that sum to one divided instead of
  multiplied (1), and a retry rate of exactly 0.5 compared the other way (1).
- **`src/Ucp`, run 1 fell under the ratchet: 68.92 %** against 69.34 %. Four
  survivors were new, all in the `CountedAs` filter this phase added: nothing
  called `search_catalog` over REST, and nothing checked that a failure counted
  is still thrown — a filter that swallowed it would have answered the agent
  with an empty 200. Both closed in `AgentToolCallMetricsTests` (two mutations,
  two kills). The other two are **equivalent**: the fallback when a result is not
  a `Results<…>` union (all three endpoints return one) and `< 500` against
  `<= 500` (no handler returns a 500; a failure is thrown). **Run 2: 70.95 %**,
  43 survivors, the baseline: those two, and the 41 of phase 9 written down above.
- **`src/Search`, re-derived: 82.07 %** (83 survivors, 203 without coverage).
  81 are survivors classified on 2026-09-17, matched by file, mutator and line;
  the 36 that were there and are not are layer B's, gone with it, and the gaps
  closed since. The 2 new ones are a counter's `unit` and `description` in a
  static initializer: **runner artefacts**, and `TrackingPlanTests` holds the unit.
- **Scoped runs.** `src/SharedKernel` (`Actor`, `AgentToolCalls`,
  `MetricLabels`): 93.33 %, 1 survivor, a `StringBuilder` capacity hint —
  **equivalent**. `src/Api` (`HttpActorAccessor`, `SyntheticTraffic`, the
  telemetry configuration endpoint): 96.30 %, 1 survivor, the accessor's per-scope
  cache of a deterministic answer — **equivalent**. `src/Catalog`
  (`ProductsPublished`): 4 of 4 killed. `src/Inventory` (`ReservationTransitions`):
  40 %, the 3 survivors are the counter's name, unit and description in a static
  initializer — **runner artefacts**, the first two held by `TrackingPlanTests`.
  `src/Ordering` (the checkout, cart and return meters): 91.30 %, 2 survivors,
  both on the `discounted` label, which **nothing asserted anywhere**. The loop in
  `CheckoutLoopTests` now asserts it on the order placed and on an idempotent
  replay — the only path where an order comes back without a priced cart — and
  the replay kills one. **Written down, waiting**: the other, which needs an order
  placed with no promotion and then replayed; the sample promotions discount
  every cart that test builds.
- **By hand, where Stryker cannot reach.** `ToolCallCounting` in `src/Mcp`:
  answered and refused swapped, a refusal counted as answered, a failure counted
  as refused — three mutations, three kills. `SetExemplarFilter(TraceBased)` in
  `src/ServiceDefaults` is **written down**: one line of configuration, seen
  working in Grafana in `P9b-3`, and a test would read the SDK's internal state.
- **What the runs cost, and what they are for.** The question the author asked
  while they ran — *does it need to run this many times?* — had two answers. The
  Monte Carlo test did not need its 200 000 walks, and now has 50 000. And the
  runs themselves are the price of a phase close, not of a pull request: the
  alternative was the three gaps above reaching the next phase unseen.

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
- **A fake has to refuse what the real server refuses.** On 2026-09-17 the first
  real run of the scope classifier (ADR 0032) failed before any verdict: Text
  Embeddings Inference accepts 32 texts per request and one culture's anchors are
  133. Every unit test was green, because the fake generator accepted any batch.
  The fake now throws above 32 like the server does, and a test proves the anchors
  are sent in batches it accepts.

## The design system's rules, made executable

Four vitest suites in `shared-i18n`, which is simply the library that already has
vitest configured. They need no browser and no container, and they read the
repository rather than the apps, because `design/DESIGN.md` governs four surfaces
and only two of them are Angular.

- `design-tokens.spec.ts` — a colour is written down in `design/tokens.css` and nowhere else. It walks every `.css` in the repository.
- `design-mockups.spec.ts` — a `.pen` or its `.html` export anywhere under `design/` names only colours the system owns, unless a person declared the exception in `proposals.json` with a reason (ADR 0033). It walks the folder whole rather than naming rungs, because the first sketch landed outside the folder the rule named.
- `design-contrast.spec.ts` — every text token clears 4.5:1 against every surface it can sit on, every fill pairing clears 4.5:1, and the focus ring clears 3:1, **in both themes** (ADR 0034). It resolves `var()` chains the way the browser does, so an alias like `--focus-ring: var(--accent)` is measured against the accent that actually wins under `[data-theme='dark']`. The one text allowed on a ramp's middle step is the brand's two wordmarks, held to the large-text bar only because the same spec holds their colour, their weight, their size and `--text-lg` itself.
- `design-css-integrity.spec.ts` — a `var()` with **no fallback** names a property that exists, every `var()` opens on a property reference, and parentheses balance. A `var(--x, fallback)` whose property is undefined is fine and stays: it degrades on purpose.

The fourth was written the same day and for a humbler reason than the third: a
script migrating 51 declarations spliced two of them wrong, and **a browser drops
a malformed declaration in silence**. The build was clean, eslint has no opinion
about CSS, and `design-tokens.spec.ts` looks for hex literals, of which a mangled
`var()` has none. The three gates that existed all passed on a language switch
that had stopped painting its own background.

It is also the clearest case in this repository of a test that had to be
disproved before it could be trusted. The first draft killed one of the three
mutations it was written for and was green — its regex demanded a `,` or `)`
after the property name, and the corruption leaves a `:` there. The attribution,
run one spec at a time, is in the file:

| mutation | integrity | contrast |
|---|---|---|
| `.lang--active` corrupted | **red** | green |
| cart badge corrupted | **red** | green |
| `--on-accent` out of `:root` | green | **red** |

Neither is enough alone, and the integrity spec's blindness — `defined` is
theme-blind, so a token declared only in the dark block satisfies it — is written
into the file rather than papered over with a second set of rules there.

The third one exists because the first two could both pass on a palette nobody can
read: keeping every colour in one file says nothing about whether those colours
work against each other. It was added with the accessibility pass that found white
on the accent at 3.87:1 on the storefront's one clay action, and a `--on-accent`
that was referenced by a component and defined nowhere — 2.45:1, and invisible to
every gate the repository owned, because an undefined custom property is not a
hex literal anywhere.

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
