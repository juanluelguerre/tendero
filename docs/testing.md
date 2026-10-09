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
| `src/Search` | `Search.Tests`, `SearchEval.Tests` | 83.38 % | 59.53 % | 2026-10-08 · phase 13a's close, one run, the model-held tests left out; eleven gaps closed after it |
| `src/Ucp` | `Ucp.Tests`, `Api.Tests` | 70.95 % | 62.50 % | 2026-09-26 |
| `tools/TrafficSim` | `TrafficSim.Tests` | 87.66 % | 87.10 % | 2026-09-26 · `Model/`, `Scheduling/`, `Queries/` |
| `src/Knowledge` | `Knowledge.Tests` | 77.34 % | 67.91 % | 2026-10-09 · phase 13a's close, with the picture reading in it |
| `src/Accounts` | `Accounts.Tests` | 80.08 % | 58.31 % | 2026-09-29 · R-7/R-8 close, first run 75.52 % |
| `src/Pricing` | `Pricing.Tests` | 83.06 % | 62.59 % | 2026-09-29 · first baseline, first run 81.41 % |
| `src/Assist` | `Assist.Tests` | 86.31 % | 79.42 % | 2026-10-09 · phase 13a's close, with the alt-text job in it; first run 85.30 %, under phase 12b's 86.26 % |
| `src/AI` | `AI.Tests` | 75.00 % | 58.54 % | 2026-10-09 · phase 13a's close; first run 71.88 %, under phase 12's 72.02 % |

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


**Phase 11's close (2026-09-27)** — run under a memory ceiling. The host stopped two
background runs because the machine was nearly out of memory: a terminal holding
more than 30 GB, not Stryker. So every run went in the foreground, **scoped to what
the phase wrote**, and the whole-project Ucp run (1 369 mutants) did not happen.
The Ucp ratchet stays at its 2026-09-26 baseline and is **re-measured at the next
close, whole**. Reports are in `docs/blog/reports/2026-09-27-phase-11/stryker/`.

- **`src/Accounts`, first baseline.** First run **61.43 %**, 54 survivors. The
  agent registry's were **real gaps**: suspending and reinstating raised events
  nothing checked; a trust change to the same level must raise none; a blank
  id, name, operator or reason must be refused; plus the registration's audit
  reason and validators, and the denials panel's edge cases (no rule, a colon in
  the detail, ties, a type that is not a command). All closed. **Run 2:
  74.29 %**, 36 survivors, the baseline:
  - **equivalent**: the null guards on the clock (a null clock fails anyway, with
    another exception) and the empty-string defaults EF needs;
  - **noise**: exception messages and span names;
  - **written down, waiting**: the phase-7 survivors in `Customer` and
    `LinkIdentity` (events raised and not asserted, `Touch`, and the handler's
    two `SaveChanges` and guest checks). They are real, small, and the next
    time Accounts changes they are closed first.
- **`src/SharedKernel`, scoped** (`MandateScope`, `Dashboard`, `Principal`):
  **88.89 %**, 1 survivor. `AgentStanding.Unknown` could have been declared
  suspended and only Ordering's tests would have noticed, so the kernel's own
  suite now says what an unregistered agent is, and what a dashboard window
  covers. Hand mutation, red.
- **`src/Ordering`, scoped to phase 11** (`AgentPolicy`, `Dashboard`,
  `CheckoutSession`, `TestOrders`), with `Ordering.Tests`. A run that mutates with
  the Postgres suite would start a database per mutant, so the integration tests
  hold the handlers and this run holds the units. First run **68.00 %**, 48
  survivors, most in the dashboard's arithmetic, and the lesson is worth the
  article: **several of my own tests were symmetric**. One new and one returning
  buyer, one of two orders mandated, carts that all expired at the same instant —
  each was a case where a mutant that swapped the two answers gave the same
  numbers. They are now asymmetric, and cover ties, windows and sums across
  orders. `CheckoutSession` gained the owner, the replaced codes and the cancel
  event. **Run 3: 84.00 %**, 24 survivors:
  - **equivalent**: one currency for `Concat`/`Except`, and `First` over a
    non-empty group;
  - **boundaries accepted**: a return opened or refunded exactly on the window's
    edge (the window is half-open, `[from, to)`);
  - **noise or runner artefacts**: the denial details and the counters' name,
    unit and description in static initializers, which `TrackingPlanTests` holds.
- **`src/Ucp`, scoped in two parts**, with `Ucp.Tests`:
  - **`Signing/` and `Ap2/`**: **74.22 % → 83.90 %**. It found a **real bug**: a
    JWK whose point is not on the curve throws `PlatformNotSupportedException` on
    Windows, which `ToEcdsa` did not catch. A malformed key in a platform's
    profile would have crashed the verifier instead of being skipped, as UCP
    asks. Fixed, with a test. Also closed:
    - JCS's layout at its boundaries (`0.5`, `0.0001`, a space);
    - keys of another type and a P-384 key;
    - what is not a compact JWS;
    - SD-JWT presentations without key binding or without a tilde, what is
      disclosed and what is not, and a key binding over other disclosures;
    - that the terms compare the line, the item and its unit price.

    Survivors: **equivalent** — output keys renamed alike on both sides of the
    terms, a `Remove` or `??=` overwritten a line later, logical forms with the
    same outcome. **Noise** — texts and the development platform. **Written down**
    — three `catch`-block removals whose survival is not explained yet.
  - **`Checkout/` operations and mapper, the knowledge extension,
    negotiation**: **58.13 % → 84.13 %**. The mapper had never been read in
    English by a unit test, and nothing checked:
    - the messages' JSONPaths;
    - automatic discounts;
    - half-written addresses;
    - that completion refuses another handler's instrument even with a token.

    All closed, plus a platform profile's `keys` read by the parser (every AP2
    test had built its profile by hand; hand mutation, red). Survivors: the other
    texts' Spanish variants and error codes (**noise**); `First` over lists that
    are never empty (**equivalent**); the 201 of a create, held by `Api.Tests`
    (**another suite**).
- **Not mutated, and why.** `src/DevIssuer`'s token exchange and `src/Mcp`'s
  checkout tools were proven by hand when they were written (P11-3, P11-1;
  mutations listed in their commits). The frontend's dashboard figures by hand,
  in P11-7's commit.
- **Also found while running: the flaky schema failures.** A full `Ucp.Tests` run
  failed a handful of validations with "could not resolve pagination.json" twice
  in phase 11, and never in isolation. JsonSchema.Net's global registry is written
  while a schema is evaluated, and seven classes evaluated in parallel. They now
  share one xUnit collection, and three consecutive runs are green.

**The redesign's close (2026-09-28)** — scoped to the backend the redesign wrote,
because the redesign is a frontend lifecycle and its few server slices are new.
Reports are in `docs/blog/reports/2026-09-28-redesign/stryker/`.

- **`src/Catalog`, scoped** (`WithdrawProduct`, `ReviewProduct`,
  `ProductAttributes`), with `Catalog.Tests`. First run **90.48 %**, 2 survivors,
  both **real gaps**: nothing asserted the status a withdrawal answers with when
  there is no product (`"unknown"`), and nothing read a defined attribute by its
  label in the culture asked for, so the fallback to the bare code survived.
  Both closed. **Run 2: 100 %** of covered. The 25 without coverage are the
  validators, the endpoints and the audit reasons, which `Api.Tests` holds and
  this run does not load (**another suite**).
- **`src/Ordering`, scoped** (`ListOrders`, `Dashboard`), with `Ordering.Tests`
  only. The configuration also loads `Integration.Tests`, which needs Docker, and
  a run with Docker stopped errors before it mutates anything; the database's
  filtering is held by `OrderListFilterTests` against Postgres, not here. First
  run **89.41 %**, 9 survivors. Three were **real gaps** in the one projection
  every orders list uses: authorised and captured could have been the same
  boolean, and the preview could have skipped the first line instead of taking
  the first three. Closed in `ListOrdersTests`; hand mutation (both at once),
  two tests red. **Run 2: 92.94 %**, 6 survivors, all in `Dashboard` and all
  already classified at phase 11's close: `Concat`/`Except` over one currency
  and `First` over a non-empty group (**equivalent**), and the returns window's
  edges (**boundaries accepted**, the window is half-open).
- **Not mutated, and why.** The redesign's Angular code has no mutation runner;
  its behaviour is held by the unit specs and the end-to-end tests named in the
  spec, and its hand mutations are listed in the commits that wrote them.

**R-7 and R-8's close (2026-09-29)** — the lists that grow, the shop's filters,
the home that sells and the campaigns behind it. Whole projects where the phases
wrote a context's new behaviour (**Pricing**, which became a writer with ADR
0044; **Accounts**, the customer's trail; **Search**, the facets and the
suggestions); scoped to the files they touched elsewhere. Every run alone, one
after the other. Reports are in `docs/blog/reports/2026-09-29-r7-r8/stryker/`.

| project | mutated with | run 1 | run 2 | survivors |
|---|---|---:|---:|---:|
| `src/Pricing`, whole | `Pricing.Tests` | 81.41 % | **83.06 %** | 79 → 72 |
| `src/Accounts`, whole | `Accounts.Tests` | 75.52 % | **80.08 %** | 59 → 48 |
| `src/Search`, whole | `Search.Tests`, `SearchEval.Tests` | 79.11 % | **83.21 %** | 164 → 132 |
| `src/Ordering`, scoped | `Ordering.Tests`, `Integration.Tests` | 77.03 % | 83.33 % | 34 → 25 |
| `src/Inventory`, scoped | `Inventory.Tests`, `Integration.Tests` | 81.82 % | 95.45 % | 4 → 1 |
| `src/Catalog`, scoped | `Catalog.Tests` | 71.55 % | 75.86 % | 33 → 28 |
| `src/Api`, scoped | `Api.Tests` | 78.57 % | 100 % | 3 → 0 |
| `src/SharedKernel`, scoped | its three suites | 75.68 % | — | 9 |
| `src/Knowledge`, scoped | `Knowledge.Tests` | 71.43 % | — | 20 |

**Search fell under its ratchet, and that was the finding.** Run 1 gave 79.11 %
against the 82.07 % baseline, and the close could not be called one: `ProductFacets.cs`, the 739 lines R-7.3 wrote,
held 51 of the 164 survivors. The facets' pure half was tested for its main
paths and not for its edges. Closed, each with a test and each seen red under a
hand mutation first:

- **The price filter's bounds** — the request carried a range on `priceAmount`,
  and nothing read its `gte` and `lte`: an unbounded range passed.
- **A choice is a code and a value** — `"COLOR:"` and `":RED"` are neither, and
  `IsAttributeCode("")` answers rather than throws.
- **The index's prefix pattern** escapes every character that is not a letter or
  a digit, so a code is read literally.
- **Order**: categories and brands by count with a tie broken by name, codes the
  definitions do not know after the ones they do and in their own order, and a
  code's options in the order the shopkeeper gave — the test data's options were
  all alphabetical, so no test could tell the definition's order from the
  alphabet's.
- **The definition decides an axis**, the index's flag only when there is none.
- **The price facet stays when either end alone is chosen**, and a band is
  chosen only by both its ends; a band the aggregation gave no count for counts
  zero; a percentile that rounds onto the dearest price is not a cut.
- **A value no product carries is not offered**, and a yes-or-no that narrows
  nothing is shown only when something of it — even "no" — is chosen.
- **The built-in facets' Spanish labels**: every test read them in English. The
  hand mutation that emptied *Marca* stayed green until a test asked in Spanish.
  Stryker still reports those labels as survivors — they are built once, in a
  static initialiser, before a mutant is switched on — so they are a **runner
  artefact now, and were a real gap before**: the survivor looked like noise
  and the hand mutation said otherwise.
- **Suggestions** (R-8): the deepest neighbourhood keeps its turns after the
  others run out (`Max` → `Min` survived because every neighbourhood in the test
  had two or three), neighbours sharing as much come by code, and two variants
  in stock at one price draw the card with the lowest id.

**Search, run 2: 83.21 %, the new baseline** (`of total` 60.06 %), 132
survivors. In the phases' code, classified:
- **Equivalent**: the price bands' rounding and cutting edges — `max <= min`
  and `<` meet at an empty range, four cuts take the same four through either
  branch, the magnitude loops end at the same magnitude whichever way the first
  one overshoots (the second loop brings it back), and the candidates' order
  does not matter before an `OrderBy`. Each followed through by hand, not
  assumed. In `SimilarProducts.Spread`, `row.Count <= limit` and `turn <=
  deepest` (the inner checks stop both).
- **Runner artefacts**: the facets' labels (above), and the search validator's
  every rule — the tests build it once as a `static` field, and a hand
  mutation that removed the filters' hook turned seven tests red.
- **Closed after run 2**, each seen red under a hand mutation: the re-rank of
  the suggestions is skipped when no product viewed has two candidates, and
  runs when one does beside others that do not (`Count > 1` → `>= 1`, `Any` →
  `All`).
- **Written down**: the suggestions' degradation counter (the hybrid search's
  tests hold `SearchDegradation`; this call site is not asserted).

The other survivors predate the phases and are **written down** as before: the
adapters' registration extensions (reranker, embeddings, Qdrant), the analysers
and boosted fields in static initialisers (**runner artefacts**), the reindex
and the vector projection's logging and batch edges, and the evaluation's
metrics at their boundaries.

**Pricing's first baseline, 83.06 %.** The phase's code (campaigns) closed its
real gaps: the lineup's last tie-break by code; a name blank in one language, and
the longest name; a RIFF file that is not WebP and the shortest JPEG; a seed file
that says `null`; and the home's most frequent answer — nothing near — reading no
promotions at all. Left, the campaign's four exception messages (**noise**). The
other 68 predate the phase and are **written down**, in three groups: the
fingerprints and canonical forms that key the quote cache (order direction and
the `"-"` placeholders — a mutated fingerprint is still deterministic, but
`cat=-` for every category would let two promotions share a key, which is a real
gap); the rule reasons' wording in both languages, which no test reads; and the
engine's edges — a buy-X-get-Y with `free > 1` (every test has `free = 1`, where
`/ free` and `* free` agree), the discount cap at exactly the room left, and the
shipping promotion on a cart that already ships free.

**Accounts, 80.08 %, the new baseline.** The trail's real gaps: the edge of the
ninety days when a merge drops what it already kept, the order of two entries at
one instant, the search's length at 2 and 200, which field and which message a
refused record or forget names, and that a change is saved. **Equivalent**: the
clamp to `now` (`>` and `>=` meet at the same instant), the merge's first
retention filter (the second applies the same bound to everything, and the
count is taken after it — proved by a hand mutation that stayed green), and an
ordinal comparer over codes already upper-cased. `IsInEnum` is **held by the
wire** (ADR 0043: a body refuses a number before a validator sees it). The span
names are **noise**, and `BrowsingTrail.Empty`'s initialiser a **runner
artefact**.

**Scoped runs.**
- **Ordering**: the trends' last tie-break by key, and **the returns validator,
  which had no test anywhere** — ADR 0043 had removed the handler's own 400s and
  left it the only gate. Closed. Left: the dashboard's five, classified at phase
  11's close; the span names (**noise**); `First` over a group that is never
  empty (**equivalent**); and the returns' audit reason and outcome, which no
  test reads (**written down**, returns' code).
- **Inventory**: the restock line at exactly five, and a warehouse no longer in
  the file named by its code. Left: `IsInEnum` (**held by the wire**).
- **Catalog**: a swatch with one digit that is not hex (`#233A5G` — the test had
  `#GGGGGG`, where `All` and `Any` agree), an option added twice, two hundred
  SKUs as one request, and the listing's paging contract. Left: the swatch
  refusal's message (**noise**), the meters' texts (**runner artefacts**), the
  culture rule (**equivalent**: culture negotiation only ever hands `es` or
  `en`), and the attribute definition's aliases and option lookups, which predate
  the phases (**written down**).
- **Api**: an exception that is not a binding's is left to the handler that owns
  it (a search engine down stays a 503, never a swallowed 500), and the refusal
  is problem details titled as the caller's mistake. `EnumsAreTheirNames`'s
  nullable branch, a survivor, went red under a hand mutation against the
  OpenAPI document test: a **runner artefact** (the document is built once).
- **SharedKernel**: `WireJson`'s `allowIntegerValues: false` survived, and a hand
  mutation to `true` turned two tests red — the converter is built once in a
  static initialiser: a **runner artefact**. The dispatchers' survivors predate
  the phases: argument guards (**noise**), `??=` on the validation context
  (**equivalent**), and two that are real and **written down** — two validators
  failing at once would keep only the last's failures if `failures ??= []` became
  `=`, and a request that is a record implements `IEquatable<T>`, a second
  generic interface the wrapper's lookup must not pick.
- **Knowledge**: nothing in the code R-7 wrote survived (`WeakSupport`, the
  queue's paging); the 20 are the claim's guards and messages, which predate it.

**What the runs cost, and one that ran twice.** Search took an hour on its own.
It failed twice before that with `MSB3027`, a DLL "locked by .NET Host": the
first queue, launched under a monitor and stopped, had left its script running,
and its Search run held an MTP test server open on `SearchEval.Tests/bin` for an
hour while the real queue ran beside it. Found by the process's command line and
its parent, stopped, and its partial reports deleted; every figure above comes
from a run that had the machine to itself.

**R-9's close (2026-09-30)** — the dashboard: the daily series, the sales mix,
the defect and late shipment rates, stock coverage and sell-through, the
campaign's baseline and search's status. No context was rewritten whole, so
every run is **scoped to the files the phase wrote**
(`-m 'Features/Dashboard/*.cs'` and the like) and sets no baseline. Every run
alone, one after the other. Reports are in
`docs/blog/reports/2026-09-29-r9-dashboard/stryker/`.

| project | mutated with | run 1 | run 2 | survivors |
|---|---|---:|---:|---:|
| `src/Ordering`, scoped (`Features/Dashboard/`, `Order.cs`, `OrderChannel.cs`) | `Ordering.Tests`, `Integration.Tests` | 82.18 % | **88.48 %** | 49 → 31 |
| `src/Inventory`, scoped (`StockHealth.cs`) | `Inventory.Tests`, `Integration.Tests` | 63.79 % | **94.83 %** | 21 → 3 |
| `src/Search`, scoped (`Health/`, `GetSearchStatus.cs`, the Qdrant and Elasticsearch adapters) | `Search.Tests`, `SearchEval.Tests` | 76.00 % | **88.57 %** | 6 → 4 |
| `src/SharedKernel`, scoped (`ShopOptions.cs`, `HealthTags.cs`) | its three suites | 0 % | — | 1 |

**The tests the dashboard had were written for its main paths, and Stryker
found the rest.** Closed, each with a test the survivor itself proved red:

- **The campaign's totals were never read.** The test of a Saturday's uplift of
  70 computed `Sold - Baseline` itself and never asserted `Uplift`: `sold -
  expected` survived as `sold + expected`, and the totals' sums survived as
  `Max`. Now a test reads the three totals over two ended days, 400 − 150 = 250.
- **The edges of the shop's day**: an order at midnight opens its day, a first
  order exactly four weeks before the opening is history enough, and at midnight
  the day before has ended while the new one is under way — not still to come.
- **A defect that is not one, and one hidden among others**: a return the shop
  rejected was never tested as *no defect*, although the comment said so, and a
  return with one damaged line among others was never tested as one.
- **The late shipment window's edges**, for the orders with their moments and
  for those counted apart; and **an order with only one of its moments**, which
  has no lead time and is counted apart too.
- **Stock**: a SKU in two warehouses covers what both hold (`Sum` survived as
  `Max`); a warehouse's median over an even count meets in the middle and leaves
  a sold-out SKU out; fourteen days is not *under two weeks*; a sold-out SKU is
  not one that *sits*; the fastest list holds only what sold and the slowest only
  what is left, and ties go by SKU.
- **Search's publisher had no test at all**: each check's name lands on its
  stage, one with no state is down, and the API's other checks are left alone;
  meaning and reranking are up when they answer and down when they do not.
- **The currency was four copies**, each surviving a mutant that always answered
  euros, because the laboratory sells in euros only. One `DashboardCurrency` now,
  with a test in dollars.

**Left, and what each is.**
- **Equivalent**: a day still to come sells nothing whether or not it is asked
  (`future ? 0m : SoldOn(day)`); `Days` is only ever called with stock above
  zero, so its `<= 0` guard cannot tell `<` from `<=`; and a median of two or
  three values is the same sorted either way.
- **Noise**: the span names, the span helper's body, the reranker's probe text
  and its two reasons.
- **Held by other suites**: the endpoints' mapping, by `Api.Tests` (the routes'
  policy and period); the Qdrant and Elasticsearch adapters, by
  `Integration.Tests` against real containers — why Search's `of total` is 28 %.
- **Held by configuration**: `ShopOptions`' default zone, which `appsettings.json`
  sets.
- **Predate R-9**: `Order.cs`'s guards, messages, taxes and payment (20, run 2 had
  no timeouts where run 1 had four), and the dashboard's returns windows and the
  currency's `Concat`, classified at phase 11's close.

**Phase 12's close (2026-10-05)** — Steward: the `Assist` context and the model
port (`AI`), both new and so mutated whole, each setting its first baseline; and
five projects the phase touched in a few files, each **scoped to those files** and
setting none. Every run alone, one after the other. Reports are in
`docs/blog/reports/2026-10-05-phase-12-steward/stryker/`.

| project | mutated with | run 1 | run 2 | survivors |
|---|---|---:|---:|---:|
| `src/Assist` | `Assist.Tests` | 67.97 % | **84.76 %** | 115 → 57 |
| `src/AI` | `AI.Tests` | **72.02 %** | — | 47 |
| `src/SharedKernel`, scoped (`Proposals.cs`, `ReadTools.cs`, `ReadLimits.cs`, `Cqrs/AuditStep.cs`) | its three suites | 77.97 % | — | 13 |
| `src/Catalog`, scoped (`CatalogueGaps/`, `FindProductsTool.cs`, `SetProductTranslation/`, `WithdrawProductProposal.cs`) | `Catalog.Tests` | 76.47 % | — | 24 |
| `src/Inventory`, scoped (`RestockList.cs`, `StockToRestockTool.cs`) | `Inventory.Tests`, `Integration.Tests` | 78.95 % | — | 8 |
| `src/Ordering`, scoped (`ProductPerformance.cs`, `ReturnsSummary.cs`, `StewardReadTools.cs`) | `Ordering.Tests`, `Integration.Tests` | 43.10 % | — | 33 |
| `src/Knowledge`, scoped (`ApproveClaims.cs`) | `Knowledge.Tests` | 100 % | — | 0 (2 killed, 6 without coverage) |

**Only `Assist` was run twice.** The tests the scoped runs and `AI`'s run asked
for were written after them and proved at level 1 instead — the mutation made by
hand, the test seen red, the code restored — and the percentages above are the
runs', not what those tests would now give. Said plainly because a second column
of dashes can read as *nothing was found*.

**What Stryker found, and what closed it.**

- **The prompt had no test at all** (`StewardPrompt`, 6 of 39 killed). The wording
  is a model's to be measured against, but four things in it are code's: the
  language it falls back to, the screen it is told (none, a page, a page and what it
  shows), that a quote in the screen cannot close the quotes it is written inside,
  and that every fixed sentence exists in both languages. Run 2: 39 of 39.
- **A rejection was never seen to be saved.** The tests read the proposal from the
  same in-memory list the handler wrote, so `SaveChanges` survived being removed.
  Now a batch that rejects saves once, and one that rejects nothing saves nothing.
- **A proposal tool's schema was never read** — the `{ items: [ payload + reason ] }`
  a grammar is built from, where S-6 had already found two bugs no test could. Its
  types, its required names and the reason's description are asserted now, and so
  is what the tools cost the window (`Sum` had survived as `Max`).
- **An answer that was only the rows.** `CopiedRows` leaving nothing falls back to
  a fixed sentence, and nothing asked for it; nor for a turn's flags when no model
  is reached, which a fallback's mutant set wrongly.
- **The tables' names are a contract with the panel, and nothing held it.** A key
  or a column renamed in a context is a heading that reads as a key in the
  backoffice, and every one of those strings survived (27 of Ordering's 33). Each
  context's tools test now states its tables as `key: column Kind, …`.
- **Sold out and not selling was last, not first — in the mutant.** The restock
  list sorts sold out first; the only test had its sold-out SKU selling, so cover
  alone put it first and `Available > 0` survived as `>= 0`. A SKU with no stock
  and no sales is asked for now.
- **Returns were summed nowhere a test looked.** A SKU in two returns, a reason on
  two lines: `Sum` survived as `Max` twice. And a reason nobody gave was never
  tested as *not a row* in what a model reads.
- **`ModelCall.Options` could replace a caller's options** (`??=` survived as
  `=`): the caller's temperature and what it had already said of the call are
  asserted kept.

**Left, and what each is.**

- **Not mutated at all: `ProposeActions.cs`.** Its 48 mutants are compile errors —
  Stryker's safe mode removes every mutation in a method it cannot compile one of —
  so the file has no score, not a good one. So it was mutated by hand at this
  close, four times: a refused check stored (red, five tests), the snapshot not
  stored (red), a refused row losing its place (red) — and **the batch saved only
  when every row was stored, which survived**: no test had a batch with one row
  accepted and one refused *and* looked at what was saved. It does now, and the
  mutation is red. The same safe mode took 163 of Assist's mutants and 151 of the
  kernel's, which is why a percentage is never the whole of a close.
- **Runner artefacts**: a `static readonly` initialiser is evaluated once, before
  a mutant is switched on — `ListProposals`' filter table, the meters' names and
  descriptions in `StewardTurn`, `MeteringChatClient` and `AIInvocationLog`,
  `ProposalApplication.Done`.
- **Equivalent**: `Filter == All ? null : Statuses[All]` (every status either
  way); a result cut at exactly its maximum length; `cost > 0` against `>= 0`
  before adding it to a counter; a warehouse's `Sum` over a SKU's shelves, of
  which it has one; `(body.Ids ?? [])` behind a validator that refuses both.
- **Estimates, by design**: the turn's token arithmetic — four characters a token,
  `Sum` of what was said, the smallest candidate's need, the 80 % that says *near
  the limit*, `>` against `>=` at the budgets. An estimate the window's margin
  absorbs; a boundary test would pin a number nobody should rely on.
- **Noise**: guard clauses on arguments (`ThrowIfNull`, `ThrowIfNullOrWhiteSpace`),
  exception and refusal wordings, span and agent names, a JSON writer's indentation.
- **Held by other suites**: the composition roots' `TryAdd` defaults, by
  `Api.Tests`; a query validator's `IsInEnum`, by the wire's closed sets (ADR
  0043) and `StewardReadsBindingTests`; the profile's bearer key and its timeout,
  by the contract suite a real endpoint runs.
- **A test double in a production assembly**: `ScriptedChatClient` (17 of AI's
  47). It is how CI's browser spec and the suite's dry run have a model, so it
  ships; its survivors are its own corners (a delay of zero, a path into an array's
  end), not the shop's behaviour.
- **Found by looking, not by a mutant**: the panel's table was never seen at the
  panel's width until the close's captures — columns cut, a name one word a line, 42
  rows in a conversation — and its `@defer` waited for an idle browser. Fixed with
  the component's own specs (a column nothing fills is not drawn; eight rows, then
  the rest on a press); the layout itself is held by the pictures in
  `docs/blog/reports/2026-10-05-phase-12-steward/screens/`, not by a test.
- **Real, and waiting**: an approval retried (`proposal.Retry` removed survives —
  the retry's own effect is not asserted beyond the command having run); the
  worker's batch cleared after a stop; the audit's redaction reading inherited
  attributes; the role of an earlier assistant message in a turn's history. Small,
  written here rather than closed at this close.

**Phase 12b's close (2026-10-06)** — Tino, the storefront's assistant: `Assist`
whole, because Tino is a third of it now, and six projects the phase touched in a
few files, each **scoped to those files** and setting no baseline. Every run alone,
one after the other, with no stack running — the first attempt shared the machine
with one and had not finished after 85 minutes. Reports are in
`docs/blog/reports/2026-10-05-phase-12b-tino/stryker/`.

| project | mutated with | run 1 | run 2 | survivors |
|---|---|---:|---:|---:|
| `src/Assist` | `Assist.Tests` | 84.69 % | **86.26 %** | 100 → 90 |
| `src/SharedKernel`, scoped (`ReadTools.cs`, `ReadLimits.cs`, `Sentences.cs`) | its three suites | 96.55 % | — | 2 |
| `src/Search`, scoped (`SearchProductsTool.cs`) | `Search.Tests`, `SearchEval.Tests` | 100 % | — | 0 (1 without coverage) |
| `src/Catalog`, scoped (`GetProductTool.cs`) | `Catalog.Tests` | 72.73 % | — | 3 |
| `src/Knowledge`, scoped (`ProductSpecsTool.cs`) | `Knowledge.Tests` | 92.59 % | — | 2 |
| `src/AI`, scoped (`SupportScoring.cs`, `ScriptedChatClient.cs`, `ModelFailures.cs`, `AIInvocation.cs`) | `AI.Tests` | 65.67 % | — | 23 |
| `src/Ordering`, scoped (`ReturnPolicyTool.cs`, `ShopperOrders.cs`) | `Ordering.Tests`, `Integration.Tests` | 73.08 % | — | 35 |

**`Assist`'s first run was under its baseline** by seven hundredths — Tino's code
had arrived with weaker tests than Steward's had been left with. **Only `Assist`
was run twice**; what the scoped runs asked for, and two gaps `Assist`'s second run
still showed, were closed after them and proved at level 1, so the percentages
above are the runs' and not what those tests would now give.

**What Stryker found, and what closed it.**

- **A turn that asked no model said nothing of whether it had fallen back** — and
  nothing checked. No scorer, switched off, no profile, a budget spent: each is now
  asserted to name no profile and no fallback, with one configured.
- **What a turn had read was lost from a turn that failed after reading**
  (`walk.Candidate?.Reads ?? []` survived as `[]`). The storefront's search starts
  from those reads; the scorer failing mid-turn now asserts they travel.
- **The scorer timing out and the shopper pressing Stop arrive as the same
  exception**, and the filter that tells them apart had no test on either side. A
  timeout is the scorer not answering; a cancellation asked for is the turn ending.
- **An order read with nothing said of it was a kind of answer** in the mutant
  (`&&` as `||`): there would have been no sentence to cite.
- **A budget's edge** (`>` survived as `>=`, twice). Both budgets are asked the same
  thing of the same turn, so where each stops answering differs by exactly what the
  budgets differ by — a test that does not need to know what a turn costs.
- **The window was asked for the longest message, not the conversation** in the
  mutant (`Sum` as `Max`): in the smallest window that holds one long message, two
  do not fit.
- **An order's sentences in English, and every state's word in either language**
  (27 of `Ordering`'s 35). The tests read the Spanish of a delivered order and one
  English sentence; a shopper writing in English could have been answered half in
  Spanish. Every state now has its word asserted in both, and a delivered order is
  read in English sentence by sentence — the one on the screen, the latest, what
  can be returned, nothing left, the day the time ended.
- **A product in no category** crashed the mutant of the product's read (`> 0` as
  `>= 0`) and nothing asked for one.

**Left, and what each is.**

- **`ScriptedChatClient`** (21 of `AI`'s 23): a test double, the model the browser
  specs and the turns' tests run on. Its survivors are its own corners — the service
  key, a regex's options, the token counts it invents. It is exercised by
  everything that uses it and tested for what the scripts rely on; mutation-testing
  a double further is testing the test.
- **Metric names, a log's subject, an exception's message** (`TinoTurn` 162, 204,
  338 to 341; `Grounding` 32; `SupportScoring` 113; `GetProductTool` 36): noise.
  `tendero.tino.turns` is held by the tracking plan's own test, by name.
- **`AnswerSchema`'s descriptions** (six string mutants): what the model is told a
  field is for. The `assistant` suite measures those words; a unit test would pin
  the wording and say nothing of whether it works.
- **Equivalent**: `orders.Length >= 0` (order facts exist only for orders read);
  `fact.Fact.Quotes ?? []` with its right side removed; the language's fallback in
  `TinoReply` (an unsupported language falls to the same sentence either way);
  `name.Length >= 0` in a premise (a product read always has a name).
- **Written down, not closed**: a claim's quotes surviving a *sin dato* turned into
  a product answer (`Grounding` 85 — the test's fact has none); the smallest tool
  budget in the window's sum (`TinoTurn` 191, one of two arithmetic mutants);
  `Sentences`' two edges; `ProductSpecsTool`'s evidence on a row with none. And
  **`TinoEndpoints` has no unit coverage** (18 mutants): the endpoint is held by
  `Api.Tests`' contract and policy tests, which this run does not mutate with.
- **Steward's survivors are phase 12's**, classified above; none is new.

**Phase 13a's close (2026-10-09)** — the images: a model that reads a picture,
a category that declares its attributes, a picture that says what it shows, and
the shop searched with one. Four projects whole — `Search`, `Knowledge`,
`Assist` and `AI` each carry a part of it — and three scoped to the phase's
files. Every run alone, one after the other, with no stack running. Reports are
in `docs/blog/reports/2026-10-06-phase-13a-images/stryker/`.

| project | mutated with | run 1 | last run | survivors |
|---|---|---:|---:|---:|
| `src/Search` | `Search.Tests`, `SearchEval.Tests` | **83.38 %** | — | 185 |
| `src/Knowledge` | `Knowledge.Tests` | 77.34 % | **77.34 %** | 165 → 165 |
| `src/Assist` | `Assist.Tests` | 85.30 % | **86.31 %** (run 3; run 2 86.18 %) | 117 → 109 |
| `src/AI` | `AI.Tests` | 71.88 % | **75.00 %** | 63 → 56 |
| `src/Catalog`, scoped (17 files of the phase) | `Catalog.Tests` | 76.35 % | — | 131 |
| `src/SharedKernel`, scoped (`ProductPictures.cs`, `Proposals.cs`, `UndescribedPictures.cs`, `WrittenLanguage.cs`) | its three suites | 89.25 % | — | 10 |
| `tools/SearchEval`, scoped (7 files of the phase) | `SearchEval.Tests` | 52.52 % | — | 132 |

**Two projects came in under their baselines**, as `Assist` had at the close
before: `Assist` by a point and `AI` by a seventh of one. Each had gained a file
of the phase with weaker tests than the project around it — the alt-text job,
the closed question. `Assist` took three runs: the second stopped eight
hundredths short, one mutant, and the third run is the one above.

**`Search` ran without its model-held tests** (`TENDERO_SIGLIP_MODELS` pointed
at a folder that is not there). With them every mutant costs the forty seconds
the model takes to load and answer, and 1,113 mutants are twelve hours. So the
resize's arithmetic in `SiglipPicture` shows nine survivors that the suite
holding a picture to the maker's embedding at 0.999998 kills where the model
is; they are counted here as what Stryker saw, not as what is untested. The run
took an hour and twenty-eight minutes as it was — on a machine shared with
another program's containers.

**What Stryker found, and what closed it.**

- **A see-through picture's blend had a test that could not tell it from the
  blend with its sign wrong.** The first test written for the survivor used
  black at half: 255 × (255 + 128) overflows a byte back to the very 127 the
  right arithmetic gives. Found by mutating by hand what the new test was
  meant to kill, and seeing it live. A dark grey a quarter opaque tells them
  apart.
- **A bound tested on one side only, three times**: a picture exactly at the
  weight limit, an upload at the height limit, and a catalogue picture at the
  height limit (`>` survived as `>=`; only width and one-over were asked).
- **The half-configured shop, again, in the registration**: removing either of
  the picture adapter's two registrations left a test that counted one
  descriptor, because the stand-in then filled the gap. Both ports are now
  resolved and are the same adapter. And an empty folder counted as a model.
- **A picture nobody can buy gave up its rank** in the mutant of the third leg
  (the pictures' list cut to what has a card even without filters), and no
  test held the rule the words' nearest list already had.
- **The thread rule was equivalent on the machine that measured it**
  (`processors * 2` clamps to eight on sixteen, as `/ 2` does). It is a
  function of its own now, held at one, two, four, twelve, sixteen and
  sixty-four.
- **A point's id was version 8 by luck**: one id was asserted, and its hash
  happened to allow the mutant. Sixty-four ids now, and the variant bits too.
- **The alt-text job's reason was compared with itself** —
  `Reason("en")` against `Reason("en")` — so the mutant that swapped the two
  languages passed. It is read as words now. Its counter of what became of
  each picture was asserted nowhere; nor that an outcome nothing happened to is
  not counted as zero; nor that the product's words leave nothing where a
  description would have been; nor that the job reads with the local profile
  unless told otherwise.
- **The closed question never proved the confidence came from the server.**
  Reading a list of token probabilities was tested; taking them from the
  response was not, and the mutant that replaced them with none survived. A
  provider's answer built with its own SDK's factory holds it, for a choice
  and for a count.
- **A doubt raised said nothing to the outbox** in the mutant, and nothing
  noticed: the event a doubt raises was asserted only for a doubt decided.
- **The same tag, and the same description, again answered *no*** in the
  mutants of `Product.Show` and `Product.DescribeImage`: the tests held that
  nothing changed, not what was answered — and *no* means *no such picture*.

**Left, and what each is.**

- **`Search`** (185). `ProductFacets` 23, `Elasticsearch.cs` 17, `SearchProducts`
  14 and the reranker's registration 18 are older than the phase and were in
  the baseline. Of the phase's: the nine in `SiglipPicture`'s resize, above;
  the worker's and the handlers' statements that only log or count
  (`ProjectProductToPictureVectors` 7, the same seven its twin for the words
  has); `SearchPicturesByText`'s validator, whose messages and property names
  survive because the tests ask whether a query is valid and not what it is
  told; `HybridSearchStrategy` L139 and L169 — **equivalent**: a union with no
  pictures is the nearest list, and a fusion with an empty third list is the
  fusion of two.
- **`Knowledge`** (165). `ReadPictures` 23: its operation names and log lines,
  two bounds redundant with the check beside them (**equivalent**), and one
  that is **a real gap, written down and not closed**: `readings.All` as `Any`
  — a picture with one reading from this model and one from an older prompt
  would be taken as current. `RulesClaimExtractor` 23, `SourceDocument` 20 and
  `ProductClaim` 16 predate the phase.
- **`Assist`** (109). In the alt-text job, three mutants on the counter's
  declaration that Stryker cannot toggle in a static initialiser (**noise**),
  two bounds redundant with the blank check beside them (**equivalent**), and
  the model's failure filter. `StewardTools` L99 — the arguments ordered
  descending instead of ascending — is **equivalent**: any one order is the
  same key. `ActionProposal` 10 and `StewardTurn` 21 are older.
- **`AI`** (56). `ScriptedChatClient` 22, the test double, as at the close
  before; the guards of `ClosedQuestion` are killed now.
- **`Catalog`, scoped** (131). `Product.cs` 48, most of them the argument guards
  and `Touch` calls of operations older than the phase, mutated here because
  the file was in scope. Of the phase's: the refusal's wording when a picture
  is told an axis the product does not have; `ForgetWhatNobodySells`'s
  `Count > 0` (**equivalent**); and the `?? []` on a picture new to a replaced
  gallery, **equivalent** — the record already turns none into an empty list,
  which the test written for it found by not failing. `ImportProducts` 16 are
  its batch counters, proven against a real Postgres by the integration
  suite and not by `Catalog.Tests`.
- **`SharedKernel`, scoped** (10): wording, and `WrittenLanguage`'s tie
  (`>` as `>=` when a sentence is as Spanish as it is English, which the
  callers treat as neither).
- **`tools/SearchEval`, scoped** (132): 109 of them strings — the usage text
  and the option names of `EvaluationOptions`, the reasons `CopilotScoring`
  writes. `PictureFloors` and what `SeedCorpus` does with pictures have no
  unit test at all: they are the measured run itself, which was run
  (`docs/search-evaluation.md`).
- **`tools/SeedImages`** has no Stryker configuration; its phase code is proven
  by the hand mutations in its commits and by the defects board.

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

### The laboratory each run needs, made by the run

The specs act on a shop, and a stack brought up from nothing has the stock seed
and nothing else. For a month they ran against the author's laboratory, where
TrafficSim had filled everything, and **eight of them failed in CI for that
reason alone** — no product on sale, no order, no agent, no claim — while locally
they passed. A spec that passes only where somebody happened to leave data is
testing the machine, not the shop.

So Playwright's `laboratory` project runs first
(`apps/backoffice-e2e/src/laboratory.setup.ts`) and makes the least every spec
needs, **only where it is missing**: the seed catalogue imported, three products
with stock published, 21 guest orders — more than a page, so the pager has a
page two — waited for until the stock saga confirms one, an active agent, and
two claims, which the workers propose by rules when no model runs. Everything
goes through the API a person would use, never the database, so the outbox, the
index and the audit see it. On a laboratory that has it all it is a few reads
(2.9 s); from nothing, 34 s.

**Proved against an empty database before it reached CI**, by pointing
Postgres at a scratch volume for one run: 25 passed, and 25 again on the second
run over the same data. That run is also what found why CI's workers had
processed nothing since 2026-09-28: the campaign seed wrote its windows at
Madrid's +01:00, Npgsql stores a `timestamptz` only at offset zero, and the
workers died at startup on every empty database — only on an empty one, because
the seed runs when there are no campaigns yet. A campaign now keeps its window
in UTC whatever offset it was written in (`CampaignTests`).

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

**Both suites run in CI, one after the other** (the storefront's since
2026-10-09). The backoffice's had been there from the start and the storefront's
was run by hand, which is to say not run: three of its specs were red for weeks,
one of them for a real defect, and it took running them for another reason to
see it. They share the stack the job has already brought up and run in
sequence, never beside each other — both move real stock in the one database.
One of the storefront's (a return opened from a delivered order) failed once in
a whole run on a development machine and passed alone; CI retries a spec once,
and a second failure there is to be read, not retried again.

## Naming

`MethodOrBehavior_Condition_Expectation` is too rigid for domain tests; prefer
sentences: `Cancelling_a_delivered_order_is_rejected()`. Readability wins.
