# Observability, product analytics and simulated traffic — the tooling

The tools weighed on 2026-09-24 for three problems Tendero has not solved yet:
seeing the system run (backend **and** both frontends), measuring what people and
agents do in the shop, and producing the traffic that makes either of those mean
anything in a laboratory with no customers.

This is an analysis, not a decision. The decision is ADR 0036 and the work is a
spec in `docs/specs/`; both cite this file for the reasons. Every version and
licence below was read on 2026-09-24 from a primary source (the LICENSE file, the
npm or NuGet registry, release notes, official docs) unless the row says
otherwise, and bundle sizes were **measured**, not quoted. What could not be
verified is listed at the end rather than smoothed over.

## 0. Three problems that share one identifier

| | Question it answers | May it be sampled? | Source of truth |
|---|---|---|---|
| **Observability** (traces, metrics, logs) | Is the system healthy, and why is it slow? | Yes | OpenTelemetry in each service |
| **Real user monitoring** (Web Vitals, JS errors, browser → API trace) | What does the person in front of the screen actually get? | Yes | OpenTelemetry in the browser |
| **Product analytics** (funnels, conversion, retention) | What do people and agents do, and does it make money? | **Never** — every conversion counts | **Domain events**, not clicks |

Large shops do not run these through one tool; they run them through one
**identifier** — a session id and a trace id carried by all three. The design
below keeps them separate and joins them by that id.

**The criteria applied to every row:**

- **Licence, by tier** (CLAUDE.md, *Licensing policy*): what the frontends link is
  tier 1 and must be permissive; what runs beside the app as an unmodified
  container is tier 2, and copyleft is acceptable there. Source-available
  licences (ELv2, FSL, BSL, custom "limited use") are flagged even in tier 2.
- **Laboratory scale**: one machine, next to an Aspire stack that already runs
  Postgres, Elasticsearch, Qdrant and the embeddings server.
- **Vendor neutrality**: the articles and the book teach the standard, not a
  vendor's console.
- **Fit with the invariants**: side effects through the outbox (7), every
  optional feature with a tested degradation path (8), OpenAPI as the single
  source of shape (11), an agent as a principal (10).

## 1. The backend: collector, stores and dashboard

### Collector

| Option | Version · licence | For | Against |
|---|---|---|---|
| **OTel Collector contrib** | v0.161.0 · Apache-2.0 | Vendor-neutral; plain YAML; OTLP/HTTP receiver takes `cors:`; ships a **`faroreceiver`**, so one collector accepts both browser adapters | The Faro receiver is **alpha** (logs, traces) and does not decode sourcemaps |
| OTel Collector core | v0.161.0 · Apache-2.0 | Smallest | **No Faro receiver** — rules out the second browser adapter |
| Grafana Alloy | v1.19.2 · Apache-2.0 | Native `faro.receiver` with no stability label (GA by Alloy's convention), sourcemaps, CORS | Its own configuration language; a second tool for the reader; ties the pipeline to one vendor |

Sources: [collector releases](https://github.com/open-telemetry/opentelemetry-collector-releases/releases) ·
[faroreceiver](https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/receiver/faroreceiver) ·
[Alloy `faro.receiver`](https://grafana.com/docs/alloy/latest/reference/components/faro/faro.receiver/)

### The all-in-one image, `grafana/otel-lgtm`

v0.33.x, Apache-2.0 for the image's own code; Grafana, Loki, Tempo and Pyroscope
inside it are AGPLv3 (tier 2). Its Dockerfile pins Grafana 13.2.2, Prometheus
3.14.0, Tempo 3.0.3, Loki 3.7.8 and **OTel Collector contrib — not
Alloy** (the released 0.33.1 ships contrib 0.160.0, measured; `main` already pins 0.161.0). The Faro receiver is therefore already in the binary; the default
`otelcol-config.yaml` simply does not enable it, and the README documents mounting
your own. Prometheus already runs with the OTLP receiver, exemplar storage and
native histograms. The README calls the image "intended for development, demo,
and testing environments" — which is exactly what Tendero is.
[docker-otel-lgtm](https://github.com/grafana/docker-otel-lgtm)

### Stores

| Signal | Option | Version · licence | Verdict |
|---|---|---|---|
| Metrics | **Prometheus** | 3.14.0 · Apache-2.0 | Native OTLP ingest (`--web.enable-otlp-receiver`), exemplars, native histograms stable. Single node, the most didactic |
| | Grafana Mimir | 3.2.1 · AGPLv3 | From 3.0 its preferred architecture is ingest storage on **Kafka**. Machinery for a problem we do not have |
| | VictoriaMetrics | v1.152.0 · Apache-2.0 | Native OTLP, but its OTLP docs do not mention exemplars — the metric → trace link is the point |
| Traces | **Tempo** | 3.0.3 · AGPLv3 | Single binary still runs without Kafka; TraceQL metrics GA; native links to Loki and to Prometheus exemplars |
| | Jaeger v2 | 2.21.0 · Apache-2.0 | Built on the OTel Collector, but its UI is traces only; the drill-down still needs Grafana |
| Logs | **Loki** | 3.7.8 · AGPLv3 | Native OTLP at `/otlp`; trace ↔ logs through derived fields |
| | Reuse our Elasticsearch | — | The official **binaries** are still Elastic License 2.0 (the AGPL option added in 2024 covers the source); Kibana/APM features are ELv2 only. And it mixes telemetry into the cluster whose job is the lexical fallback |
| | OpenSearch | 3.8.0 · Apache-2.0 | Heavy for logs at this scale; weaker trace ↔ log integration than Grafana's |

### All-in-one alternatives, named so the choice is visible

| Option | Licence | Why not |
|---|---|---|
| SigNoz v0.143.0 | MIT **except** `ee/` and `cmd/enterprise/` (proprietary); the default image is built from `cmd/enterprise` — the MIT build is `signoz-community` | A licence trap in the default image |
| ClickStack / HyperDX v2.39.1 | UI MIT; ClickHouse and its collector Apache-2.0 | The most permissive all-in-one — the name to reach for if one is ever preferred. Not Grafana, so the exemplar chain is theirs, not the standard stack's |
| Uptrace | AGPLv3; latest tag a beta | Maturity |
| OpenObserve v1.0.3 | AGPLv3 | Less common; nothing it does that the standard stack does not |
| **Aspire dashboard** | part of Aspire | Already there, already accepts browser OTLP with CORS, shows exemplars. **In memory only, no custom dashboards, no Faro.** It stays as the zero-setup default, not as the dashboard |

**Grafana** itself: 13.2.2, AGPLv3 since 2021, unchanged. From 12.4 the image is
`grafana/grafana`; `grafana-oss` is no longer updated, and `grafana-enterprise`
is under a proprietary licence (its image wrongly ships an AGPL LICENSE file —
[grafana#132178](https://github.com/grafana/grafana/issues/132178)). Use
`grafana/grafana`.

**Recommendation.** `grafana/otel-lgtm` as a tier-2 container resource in the
AppHost, with our own `otelcol-config.yaml` mounted: OTLP/HTTP CORS narrowed from
`http://*` to the two app origins, and the contrib `faro` receiver added to feed
the same Loki and Tempo pipelines. Everything is Apache or AGPL, nothing needs
Kafka or ELv2, and the configuration is small enough to print in an article.
Split it into separate containers only when the book needs to show the pieces
one at a time — the configuration carries over. Move to Alloy only if Faro
sourcemaps or a non-alpha Faro receiver becomes a requirement, by ADR.

### Two modes, one switch — Aspire is never replaced

The Aspire dashboard is not "all of this". It keeps its data in memory, has no
custom dashboards, no SLOs or alerts, no history and no SQL, and does not accept
Faro. It is excellent at what it is for — traces, logs and metrics live, with
nothing to configure — and that is exactly why it stays the default.

| Mode | What starts | Where telemetry goes |
|---|---|---|
| **Basic** (default) | today's stack | services and browser → Aspire dashboard |
| **Full** (`Tendero__Observability=true`) | + `otel-lgtm` | services and browser → collector, which **fans out to both**: the Aspire dashboard **and** Tempo, Prometheus, Loki |

The switch follows the pattern the AppHost already uses for `Tendero:Vectors`,
`Tendero:Tools` and `Tendero:Issuer`: the resource is always declared, carries
`WithExplicitStart()` when the switch is off, and the services are wired to it
**only when it is on**. A launch profile (`https-observability`) sets the variable,
so from Visual Studio it is a choice in the profile drop-down.

It is a start-time switch, not a button, for a reason worth an article paragraph:
each service's OTLP endpoint is fixed when the AppHost starts it, so starting the
container by hand from the dashboard redirects nothing — the same consequence
ADR 0023 recorded for the vector layer. The simulator, by contrast, is an
explicit-start resource in both modes: starting it later changes nothing about
anyone's wiring.

The browser always sends to a same-origin path (`/otlp/v1/*`); the proxy behind it
forwards to the Aspire dashboard or to the collector according to the mode, so the
frontends never know which mode they are in.

## 2. The browser: a port with two adapters

The frontends get a telemetry **port** (`libs/shared/telemetry`) with three
adapters — OpenTelemetry JS first, Grafana Faro second, Noop for tests, for
missing consent and for a failed start — chosen at runtime from configuration and
loaded with a dynamic `import()`. The research below is why that shape, and not a
single SDK, is the right one.

### OpenTelemetry JS

All Apache-2.0: `api` 1.9.1; `sdk-trace-web`, `sdk-trace-base`, `sdk-metrics`
2.11.0; `instrumentation-fetch`, `instrumentation-xml-http-request`,
`exporter-trace-otlp-http`, `sdk-logs`, `api-logs` 0.222.0;
`instrumentation-document-load` 0.67.0. A newer repository,
[opentelemetry-browser](https://github.com/open-telemetry/opentelemetry-browser),
adds `@opentelemetry/browser-sdk` 0.4.0 (with `session`) and
`@opentelemetry/browser-instrumentation` 0.8.1, whose every entry point — errors,
web-vitals, navigation, user-action — sits under `./experimental/*`.

- **For:** the same data model as the .NET backend; OTLP straight to the collector
  or the Aspire dashboard; the smallest bundle measured; entirely vendor-neutral.
- **Against:** the project's own README says browser instrumentation "is
  experimental and mostly unspecified". Only `exception` is a stable browser
  semantic convention; `browser.web_vital` is in development. The APIs move:
  **`@opentelemetry/api-events` is already marked "no longer supported"** — events
  go through the Logs API now. You assemble the pieces yourself.
- **Angular 22 is zoneless by default** (since v21), and this matters more than
  anything else in the row. `WebTracerProvider` defaults to `StackContextManager`,
  which carries context only through synchronous `context.with()`; the
  `context-zone` packages need zone.js; `instrumentation-user-interaction`
  declares zone.js as a peer dependency and **is dropped**. OTel's own docs say
  there is no native browser way to carry context from a click across a promise.
  What survives, and is the valuable part: fetch/XHR spans still inject
  `traceparent` and join the backend trace. Parenting a user action to its request
  is done explicitly, with `context.with()` in an `HttpClient` interceptor behind
  the port.
- **Web Vitals** are not in the core SDK: Google's `web-vitals` 6.2.2
  (Apache-2.0), directly or through the experimental instrumentation.

### Grafana Faro

`@grafana/faro-web-sdk`, `faro-web-tracing`, `faro-transport-otlp-http`,
`faro-instrumentation-replay` — all 2.12.1, Apache-2.0.

- **For:** RUM-complete out of the box — sessions, Web Vitals, errors, user
  actions, views, navigation, CSP violations. Its tracing package **is**
  OpenTelemetry (`sdk-trace-web`, `instrumentation-fetch`), and
  `faro-transport-otlp-http` converts Faro's model to OTLP, so the same collector
  endpoint can serve both adapters.
- **Against:** about **three times** the bundle of the plain OTel trace setup (below).
  **There is no Angular package** (`@grafana/faro-angular` is a 404; only React
  exists) — we integrate it ourselves, which the port does anyway. Console capture
  is on by default: a PII risk until switched off.
- **Watch on every upgrade:** `faro-web-sdk` pins `ua-parser-js` at **1.0.41
  (MIT)**. The current `ua-parser-js` 2.x is **AGPL-3.0-or-later**. The pin is the
  only thing keeping Faro's dependency tree clean for tier 1.

### Measured bundle cost

esbuild `--minify --format=esm --target=es2022`, gzip -9, measured 2026-09-24.
Angular's own tree-shaking may move these slightly.

| Setup | Minified | Gzip |
|---|---|---|
| OTel: provider, batch processor, OTLP/HTTP exporter, fetch, XHR, document-load | 88.1 KB | **27.8 KB** |
| OTel: the same + logs SDK and exporter, web-vitals, errors | 125.9 KB | **39.7 KB** |
| Faro: web-sdk, default instrumentations | 122 KB | **42.0 KB** |
| Faro: web-sdk + web-tracing | 271 KB | **88.5 KB** |

This is the first number of the declared comparison (§8), and it is already an
article sentence: the RUM features Faro gives for free cost about 49 KB gzip
against a like-for-like OTel setup — whether that buys anything the OTel adapter
cannot produce is what the seeded runs will say.

### Other SDKs, and why they are not adapters

| SDK | Licence (SDK · server) | Why not |
|---|---|---|
| Sentry 11.0.0 | MIT · **FSL-1.1** (becomes Apache after two years) | Error tracking first; the server is source-available and heavy |
| Elastic RUM 5.17.5 | MIT · ELv2 | Tied to Elastic APM, and to the cluster we keep for search |
| OpenReplay 18.2.0 | MIT · AGPLv3 (+ `ee/`) | Session replay, see §6 |
| PostHog JS 1.434.12 | Apache-2.0 AND MIT · MIT + `ee/` | Analytics and replay, not tracing (§3) |
| Honeycomb, Datadog, New Relic | Apache-2.0 | Useful only with their SaaS backend |

### Pitfalls the port has to own

- **Only one adapter per page load.** OTel's `instrumentation-fetch` and Faro's
  request monitor both wrap `fetch`/XHR, and `faro-web-tracing` registers the
  global tracer provider — a second registration fails with *"Attempted duplicate
  registration of API"*. The comparison is two runs, never both at once.
- **CORS.** Fetch/XHR inject `traceparent` only for origins listed in
  `propagateTraceHeaderCorsUrls`; the .NET API has to allow `traceparent` and
  `tracestate`, and the receiver has to allow the app origins.
- **Ad blockers.** Send telemetry to a same-origin path (`/otlp/v1/*`) proxied to
  the collector. That removes the blocking and the CORS question together.
- **Sampling.** Browser sampling is head-based; an unsampled browser span can
  suppress the backend's trace unless the backend samples on its own terms.
- **PII.** Scrub query strings and ids from `url.full` in a span processor; no
  `user.id` without consent; Faro's console capture off.

## 3. Product analytics

| Option | Licence | For | Against |
|---|---|---|---|
| **First-party** (outbox → `analytics` schema in Postgres, plus a small first-party client endpoint; Grafana over SQL views) | ours | The only option where **actor kind is first-class** — the server already knows whether a person or an agent acted (invariant 10); reuses the outbox (invariant 7); the event schema can live in OpenAPI (invariant 11); no new infrastructure | We build funnels as SQL; no replay, no experiments engine |
| PostHog self-hosted | MIT except `ee/` (paid for production) | Funnels, replay, flags, experiments in one product; a .NET SDK | Self-hosting is "officially unsupported", paid features are cloud-only; ~4 vCPU / 16 GB and ClickHouse, Kafka, Zookeeper, Postgres, Redis, MinIO. Its person is a browser — an agent checking out over UCP is a second-class citizen |
| Snowplow | Pipeline under the **Snowplow Limited Use License 1.1** (non-production or non-commercial only); trackers BSD-3 / Apache-2.0 | The reference design for event pipelines and schemas | A custom source-available licence on the core |
| RudderStack | Server **ELv2**; JS SDK MIT | CDP routing | Source-available; routes what our outbox already routes |
| Jitsu | MIT | Permissive event collector | Duplicates the outbox |
| Umami v3.4.0 | MIT | Funnels, journeys, goals, retention, revenue — the best of the light tools | Pageview-centric; cannot see a server-side actor |
| Plausible CE | AGPLv3 | Simple, privacy-first | CE lacks funnels and ecommerce revenue goals |
| Matomo | GPLv3 | CNIL-recognised for the consent exemption | PHP/MySQL stack beside ours |
| OpenPanel | AGPLv3 | Mixpanel-like, funnels | Browser-centric |
| Countly Lite | AGPLv3 **with a modified §7** (binding branding terms) | — | A custom addition to the licence |

**What the industry is doing, and why it settles the question.** Tracking has
moved server-side and warehouse-native: events land in a store the company owns
(ClickHouse Apache-2.0, DuckDB MIT, dbt-core Apache-2.0), because of ad blockers,
ITP and consent. Agentic traffic makes the move mandatory rather than fashionable.
Adobe reports AI-sourced retail traffic up 393 % year on year in Q1 2026, and
converting better than human traffic
([Adobe Digital Insights](https://business.adobe.com/resources/sdk/2026-q2-ai-traffic-report.html)).
Several practitioners report that GA4 cannot reliably tell agents from people,
and agents often run no JavaScript at all, so a pixel never sees them. Cloudflare's
Web Bot Auth, with Visa's and Mastercard's agent protocols on top, identifies an
agent by HTTP message signatures
([Cloudflare](https://blog.cloudflare.com/secure-agentic-commerce/)). The
conclusion follows directly: **the human/agent split has to be computed
server-side, from the authenticated principal** — which is the one place Tendero
already knows who acted.

**Recommendation.** First-party. Server events from the outbox carry
`actor.kind` (human / agent), the surface (web / webmcp / mcp / ucp), the agent's
id from the validated token or mandate, and the trace id. Client events (views,
searches, zero-result searches, WebMCP tool calls) go through the telemetry port to
a first-party endpoint. Umami is an optional pageview dashboard later; ClickHouse
or DuckDB only if volume ever asks, with the number recorded.

## 4. Feature flags and experiments — a phase of their own, after this one

**Tendero already has a flag port, and it is ours** (ADR 0023): `IFeatureGate` in
the SharedKernel over a closed enum `FeatureId`, every member carrying a
`[RemovalCondition]` and read in exactly one place, and one adapter,
`ConfigurationFeatureGate`. The enum is empty today on purpose — both flags left,
one for becoming configuration and one for meeting its condition.
`Microsoft.FeatureManagement` was considered there and **refused**: a closed set of
two booleans did not need targeting, filters and variants.

So the providers the author wants to compare do not replace that port; they go
**behind** it:

```
slice ──▶ IFeatureGate (SharedKernel, ours: closed set, removal conditions, one reader)
              ├─ ConfigurationFeatureGate      (today)
              └─ OpenFeatureGate ──▶ OpenFeature API (the standard)
                                        ├─ in-memory     (tests)
                                        ├─ flagd
                                        ├─ Microsoft.FeatureManagement
                                        ├─ Unleash
                                        └─ GrowthBook
```

The kernel cannot reference OpenFeature (invariant 4), and the discipline ADR 0023
buys — no flag without a condition for its death, no flag read twice — belongs to
Tendero, not to a vendor. OpenFeature's value is that **one** adapter reaches every
provider, and that the comparison is between providers rather than between
integrations.

| Option | Licence | What it represents |
|---|---|---|
| **OpenFeature** (spec, .NET and JS SDKs, contrib) | Apache-2.0 · CNCF incubating since 2023-11-21 | The standard evaluation API |
| In-memory (in the SDK) | Apache-2.0 | Tests, and the default |
| **flagd** | Apache-2.0 | OpenFeature's reference: one container reading a file |
| **Microsoft.FeatureManagement** 4.8.0 (2026-09-24) | MIT | What ".NET's flag system" means in practice: configuration-driven, no server. Not part of the runtime — .NET's *feature switches* (`AppContext`, `[FeatureSwitchDefinition]`) are for trimming, not product flags |
| Unleash | **AGPL-3.0 since 2026-05-26** (was Apache-2.0); SDKs Apache-2.0 | A full platform: UI, strategies, metrics. Tier 2 |
| GrowthBook | MIT except `enterprise/` | Flags **and experiments with statistics**, warehouse-native over our Postgres |

**A finding worth its own section.** The OpenFeature provider for
Microsoft.FeatureManagement exists — `OpenFeature.Contrib.Provider.FeatureManagement`,
Apache-2.0 — and has been **in preview since January 2024** (0.1.2-preview,
2026-04-02), never stable; the issue asking Microsoft about OpenFeature
compatibility ([FeatureManagement-Dotnet#368](https://github.com/microsoft/FeatureManagement-Dotnet/issues/368),
February 2024) has no answer. Either the preview is measured for what it lacks, or
Tendero writes its own OpenFeature provider over `IFeatureManager` — a small class,
and a good article section.

**What the port lacks for experiments.** `IsEnabled(FeatureId)` is a boolean with
no subject. An A/B experiment needs a **variant** and a **stable assignment per
subject** (the anonymous session, not the account — see §7), so the port grows an
evaluation context. That is an amendment to ADR 0023, made by the phase that needs
it, not now.

### A/B experiments, and what a simulator can and cannot prove

Two versions shown to randomly assigned, stable groups; one primary metric and
its guardrails declared before the start; a sample size computed before the start;
no peeking. An experiment **is** a flag with a removal condition — *the experiment
concludes* — which is why ADR 0023's shape fits it.

With synthetic traffic, an A/B **cannot show what real people prefer**: simulated
users prefer what the simulator's probabilities say. What it can show:

- **The machinery works.** An **A/A test** — A against itself — must *not* come out
  significant; if it does, assignment, event tagging or the statistics are broken.
  It is a gate in the sense of Part IV of the book.
- **Search is the exception.** The simulator's queries come from the golden set,
  which carries relevance judgments; if simulated users click by that relevance, a
  lexical-vs-hybrid A/B measures something real, and it ties to `SearchEval` and
  NDCG@10.

**The condition from article 09.** A flag enters only when it guards behaviour
that exists, and now also answers *which event proves it is used and worth it?* —
the same question every feature answers after this work.

## 5. Simulated traffic — "a simulation of real life"

Three things that are easy to conflate:

- **Synthetic monitoring** — a few scripted checks from probes, continuously: *is
  it up and correct?* (Grafana Synthetic Monitoring, Checkly, Datadog Synthetics).
- **Load testing** — volume against a throughput target: *where does it break?*
- **Traffic simulation** — a population of personas following a behavioural model
  over realistic time: *what would the data look like if people used it?* This is
  the one Tendero needs. Large companies get it by shadowing or replaying
  production traffic (GoReplay); with no production, the substitute is a model.

| Option | Licence | For | Against |
|---|---|---|---|
| **Playwright (Node)** reusing `frontend/apps/*-e2e` | Apache-2.0 · already a dependency (1.62.1 installed) | Reuses locators, sign-in helpers and journeys; real browsers, so real Web Vitals, JS errors and `traceparent` | The journey model lives in TypeScript |
| Microsoft.Playwright (.NET) | MIT · 1.63.0 | One language with the AppHost | Rewrites every interaction; nothing carries over |
| Grafana k6 + browser | **AGPL-3.0** (tier 2 as a separate tool) | Browser module stable since 1.0; the best arrival-rate executors; Web Vitals | A second copy of the specs in k6's own runtime; Chromium only; its OTel output is **metrics only** |
| Artillery | **MPL-2.0** (tier 2) | Its Playwright engine reuses test code, injects web-vitals, has arrival phases — the closest off-the-shelf match | Weak copyleft; another tool for the reader |
| Gatling | core Apache-2.0 | Protocol-level, JS/TS SDK | No browser; dashboards are Enterprise |
| NBomber | v5+ **free for personal use only** | — | Rejected: it would be linked |
| Locust | MIT | — | Python, outside the sidecar's remit |
| Azure Load Testing, Playwright Workspaces | SaaS | — | Cloud-only; not a self-hosted lab |

**Recommendation — one .NET simulator: people in the browser, agents over the
protocol**, the same split as the three trust models. *(Revised the same day:
the first draft chose Node Playwright to reuse the e2e suites.)*

- **Why not Node, after all.** The three e2e specs are 666 lines with three or four
  loose helpers (`signIn`, `firstProduct`, `withAgent`) and no page objects —
  little to reuse. And a test and a journey are different things: the test walks a
  fixed path and asserts; the journey draws every step and asserts nothing.
- **One scheduler, one seed.** The model has to decide in one place how many
  sessions arrive, which are people and which agents, and with which persona.
  Humans in Node and agents in .NET would be two processes and two seeds to keep
  in step, and reproducibility is the first thing that breaks.
- **Human personas**: `Microsoft.Playwright` (MIT, 1.63.0 — a **new NuGet
  dependency, approved by the author on 2026-09-24**), headless. The e2e suites today
  cover search, product page, variants, add to cart, WebMCP, the backoffice
  queue, stocktake and order transitions; **checkout and payment, returns, cart
  abandonment and publishing a product** are missing and become the simulator's
  first new journeys (and, likely, e2e tests too).
- **Agent personas**: in the same process, the `ModelContextProtocol` 2.2.0 client
  (Apache-2.0 — already in the tree through `ModelContextProtocol.AspNetCore`) and
  plain HTTP for UCP, with dev-issuer tokens. No new licence.
- **One explicit-start Aspire resource**, `tools/TrafficSim` beside `SearchEval`,
  so the stack never starts generating traffic on its own. And it fits the series:
  after *a hundred pictures, and not a line of Python*, a simulator in C#.

**The behavioural model.** Menascé's *Customer Behavior Model Graph* — each
persona a discrete-time Markov chain over page types, with transition
probabilities and think times; TPC-W was built on it. Arrivals as a
non-homogeneous Poisson process whose rate follows a 24-hour profile, compressed.
One master seed per run and a child seed per session derived by hashing
(run seed, session index), so concurrency never changes an outcome; the seed is
recorded on the run's span.

**Parameters, with their sources and their grade.** The simulator states them;
the article says plainly that the dashboard shows the simulator's assumptions,
not the market.

| Parameter | Value | Source · grade |
|---|---|---|
| Cart abandonment | 70.22 % (average of 50 studies) | [Baymard](https://baymard.com/lists/cart-abandonment-rate) · primary |
| Abandonment reasons (exit weights) | extra costs 40 %, slow delivery 20 %, card trust 19 %, forced account 18 %, long checkout 17 %, site errors 17 % | Baymard · primary |
| Conversion | ~2.66 % (range 1.6–2.9 % across sources) | Dynamic Yield · read via a summary |
| Add to cart | ~6 % | secondary |
| Search usage · searcher conversion | 30–50 % of visitors search; searchers convert 2–3× | vendor aggregators · low grade |
| Zero-result rate | < 5 % healthy, ~15 % untuned | vendor aggregators · low grade |
| Query mix | jargon, subjective ("cheap"), misspellings | [Baymard query types](https://baymard.com/blog/ecommerce-search-query-types) · primary |

The queries come from the golden set, zero-result ones included, so the search
dashboard and `SearchEval` speak about the same queries.

**Resource cost.** No primary source gives a per-session figure; the estimate is
100–300 MB per headless context sharing a browser, and 5–20 concurrent sessions
beside the Aspire stack. The simulator needs plausibility, not load — one
session a minute at night, three to five at peak is enough — and the real number
is **measured and recorded** when it runs, per the scale rule.

## 6. Labelling synthetic traffic

- OpenTelemetry defines `user_agent.synthetic.type` with values `bot` and `test`,
  at **Development** status
  ([registry](https://opentelemetry.io/docs/specs/semconv/registry/attributes/user-agent/)).
  The simulator stamps `test`.
- Precedent: Datadog Synthetics marks its requests with a User-Agent suffix and a
  header carrying the test id.
- Tendero: a User-Agent suffix, an `X-Tendero-Synthetic: <run-id>` header, and
  baggage `synthetic.run_id` / `synthetic.persona` promoted to span attributes and
  to analytics rows. **Agent personas are labelled synthetic too** — they are real
  principals with real tokens, and the label is the only thing that separates a
  simulated agent from a real one.

Everything here is synthetic today. The label exists because the design has to
be the right one for the day it is not.

## 7. Privacy (Spain and France)

- **Audience measurement can be consent-exempt** under the AEPD's 2024 guide and
  the CNIL's Sheet 16, on conditions: audience measurement only, one publisher,
  anonymous statistics not combined with other processing, no cross-site
  tracking, IP truncated, tracker life ≤ 13 months, data ≤ 25 months. *(The AEPD
  PDF returned an error; its conditions come from IAB Spain and Autocontrol
  summaries.)*
- So the client endpoint is designed to that exemption: a first-party session id,
  truncated IPs, bounded retention, **never joined to an account or an order**. A
  funnel linked to an account is not exempt and sits behind consent.
- The OTel browser session id lives in `localStorage`, which is non-essential
  storage too: it follows the same rule.
- Outbox business events are GDPR processing, not ePrivacy cookies: lawful basis
  and minimisation still apply.
- **Session replay needs prior consent** (CNIL's 2026 draft recommendation) and
  records form input, checkout included. At laboratory scale there is no real
  behaviour to replay: **left out**, and if it ever comes it is a separate,
  consent-gated adapter.
- An agent principal is not a natural person; the delegating human's data is.
  *(An inference, not a source.)*

## 8. What this adds up to

| Concern | Choice | Tier · licence |
|---|---|---|
| Collector | OTel Collector contrib, inside `grafana/otel-lgtm`, our config mounted | 2 · Apache-2.0 |
| Metrics · traces · logs | Prometheus · Tempo · Loki | 2 · Apache-2.0 · AGPLv3 · AGPLv3 |
| Dashboard | Grafana (`grafana/grafana`) | 2 · AGPLv3 |
| Dev default | Aspire dashboard, kept in both modes | — |
| Mode switch | `Tendero__Observability=true`, a launch profile, start-time | — |
| Browser, adapter 1 | OpenTelemetry JS (+ `web-vitals`) | 1 · Apache-2.0 |
| Browser, adapter 2 | Grafana Faro (tracing via OTel) | 1 · Apache-2.0, with the `ua-parser-js` pin watched |
| Browser, adapter 3 | Noop | ours |
| Product analytics | First-party: outbox + client endpoint → Postgres → Grafana | ours |
| Flags (next phase) | `IFeatureGate` (ours) → `OpenFeatureGate` → flagd, Microsoft.FeatureManagement, Unleash, GrowthBook, compared | 1/2 · Apache-2.0, MIT, AGPLv3 |
| Traffic, people | `Microsoft.Playwright` in `tools/TrafficSim` | 1 · MIT, **new, approved 2026-09-24** |
| Traffic, agents | the same process, `ModelContextProtocol` client + HTTP | 1 · Apache-2.0, already present |
| Replay | none | — |

**A declared comparison, before it runs.** Both browser adapters, the same seed,
the same traffic, two runs. Measured: bundle cost (already known, §2), each SDK's
effect on the Web Vitals it reports, what reaches Grafana and how faithfully, and
the lines of adapter code each needs. The hypothesis is written before the runs,
as ADR 0033 did for the design experiment.

## 9. Found on the way

- **Our Elasticsearch image is probably ELv2, not AGPL.** The AppHost pulls
  `docker.elastic.co/elasticsearch/elasticsearch:9.5.0`; Elastic's AGPL option
  covers the source, while the official binaries remain under the Elastic
  License 2.0. The two-tier policy speaks of copyleft; ELv2 is source-available,
  and its restriction (offering Elasticsearch as a managed service) does not bind
  Tendero — but the tier was never recorded for this image, and it should be.
- `frontend/package.json` declares `@playwright/test` `^1.36.0` while 1.62.1 is
  installed: the range should say what the lockfile does.
- Unleash moved from Apache-2.0 to AGPL-3.0 on 2026-05-26; Faro's clean tree rests
  on a pin of an MIT `ua-parser-js` whose current major is AGPL. Both are the
  kind of change CLAUDE.md warns is invisible from the package name.

## 10. Not verified

- Per-session memory and CPU of a headless Chromium context (no primary source).
- Absolute ecommerce bounce rate and session length (Contentsquare gates them);
  Dynamic Yield's 2.66 % was read through a summary; search-usage and zero-result
  figures are vendor aggregates.
- The AEPD guide's own text; the CNIL replay recommendation's final status.
- A formal stability label for Alloy's `faro.receiver`; exemplars through
  VictoriaMetrics' OTLP path; whether Prometheus still needs `exemplar-storage`.
- ~~The Faro receiver end to end against the Faro Web SDK inside `otel-lgtm`~~ —
  **proved on 2026-09-24** (`P9b-2`): accepted unchanged once CORS lists
  `content-type`, `x-faro-session-id` and `idempotency-key`.
- Grafana's claim of Angular "router detection" in Faro (no code found).
- Secondary-only: Cloudflare Radar's bot share, Shopify's stricter limits for
  unsigned agents, Google's statement on identifiable Gemini agent sessions.
- Whether k6's OTel output is still experimental; NBomber v4's licence; the exact
  licence file of GoReplay's open edition; the dbt Fusion licence.

## 11. Material for the article and the book

- **Article 05**, *Seeing it run*: both ends, two modes, and the question every
  feature now has to answer — *which event proves it is used, and that it is worth
  it?* **Article 06**, *A shop with no customers*: the simulator, the CBMG and its
  sourced parameters. Candidate section or later article: *same traffic, two SDKs*.
  (The blog was renumbered on 2026-09-24 to make room at 05 and 06.)
- **Book, Part IV** (*the brake — measuring*): chapter 22, *Seeing it
  run* — tests measure the code, telemetry measures the running system, and the
  question measures the feature. Chapter 34 (*Know your agent*) gains the refused
  agent checkout as a trace and a funnel; chapter 36 (*What it cost*) gains real
  numbers from the dashboard.
- Numbers already in hand for either: the bundle table (§2), the 49 KB gap between
  like-for-like setups, 70.22 % and its six reasons as simulator parameters, and
  the audit still to run of how much of Tendero was instrumented before this
  work — traces in nearly every slice, custom metrics in two files, the browser
  in none.
