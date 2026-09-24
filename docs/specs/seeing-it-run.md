# Spec: Seeing it run — observability for both ends, and simulated traffic

**Status**: draft
**Slice(s)**: AppHost · ServiceDefaults · `libs/shared/telemetry` · `tools/TrafficSim` · business metrics in existing slices
**Articles**: 05 · *Seeing it run*, and 06 · *A shop with no customers: simulating real life* · **Book**: chapter 22
**Decisions**: [ADR 0036](../adr/0036-observability-is-a-second-stack-behind-a-switch.md), [ADR 0037](../adr/0037-traffic-is-simulated-from-a-declared-model.md) · **Analysis**: [`observability-tooling.md`](../analysis/observability-tooling.md)

## Problem

Nobody can see Tendero run. Spans exist in nearly every slice, custom metrics in
two files and browser telemetry nowhere; everything lands in a dashboard that
forgets on restart and draws nothing it was not built to draw. There is no traffic
to see either. Phases 10 and 11 are about to build the behaviour most worth
watching — a person approving a claim, an agent checkout being refused — and this
comes first so that they are born observed instead of instrumented afterwards.

## Behavior

### A · The stack, behind a switch

- Given the AppHost starts with no switch, then everything is as today: services
  and browsers export to the Aspire dashboard, and `otel-lgtm` is declared but not
  started.
- Given `Tendero__Observability=true` (profile `https-observability`), then
  `otel-lgtm` starts with our `otelcol-config.yaml`, every service and both
  browsers export to its collector, and the collector fans out to the Aspire
  dashboard **and** to Tempo, Prometheus and Loki. The Aspire dashboard shows what
  it showed before.
- Given the full mode, when a metric panel shows a latency spike, then its
  exemplar opens the trace in Tempo and the trace opens its logs in Loki.
- Given the collector is down, then the shop serves exactly as before (exporters
  drop, nothing blocks); a test proves it.

### B · The browser

- Given either app loads, then `provideTelemetry()` reads the adapter from runtime
  configuration (`otel` | `faro` | `noop`), imports only that adapter and starts
  it; any failure falls back to Noop and the page is unchanged.
- Given a shopper clicks *Add to cart*, then one trace contains the browser span,
  the API span and the Ordering span (`traceparent` crosses; the API allows
  `traceparent` and `tracestate`).
- Given any page, then LCP, INP and CLS are reported per route; an unhandled error
  is reported through Angular's `ErrorHandler`; `url.full` carries no query string
  and no id.
- Given `navigator.modelContext` exists and a WebMCP tool is invoked, then the
  event carries `actor.kind=webmcp`.
- Given the storefront without analytics consent, then technical telemetry still
  flows without personal data, and behavioural events do not.

### C · Business metrics and the pulse

- Meters in the slices that own the facts: orders placed, payment outcomes,
  carts created and abandoned, searches and zero-result searches, products
  published, stock movements, WebMCP tool calls — each with `actor.kind` and
  surface, none with an id as a label.
- A Grafana dashboard, **Shop pulse**, on one screen: RED per service with SLO
  burn rate; Web Vitals p75 and JS errors per app; orders per minute, conversion,
  cart abandonment, zero-result rate; the degradation counter; the agent share of
  traffic and of orders. Level 1 per context and per app; level 2 through
  exemplars. Dashboards are provisioned from files in the repository.

### D · The question, as a rule

- `docs/telemetry/tracking-plan.md` (or `.yaml`): every event's name, schema, the
  question it answers and the decision its value would change.
- The frontend's event names and payloads are generated from the plan: an
  unplanned event does not compile.
- A test fails when an event emitted in `src/` is not in the plan, or an event in
  the plan is emitted nowhere.
- The existing features are audited against the question — search, filters, cart,
  checkout, publish, WebMCP — and the ones with no answer are written down, not
  hidden. The audit is the article's first half.

### E · Simulated traffic

- Given the simulator is started from the dashboard with a seed, then human
  personas browse in headless browsers and agent personas call MCP and UCP, arriving
  along a compressed daily curve, until stopped.
- Given the same seed twice, then the same sessions, in the same order, take the
  same steps.
- Every request, span and event it causes is labelled synthetic (ADR 0037).
- New journeys the model needs and the e2e suites lack: checkout and payment,
  returns, cart abandonment, publishing a product.

### F · The declared comparison

- Written before the first run: the hypothesis, the seed, the duration, and what is
  measured for the OTel and Faro adapters — bundle cost (the static half is
  measured already: 39.7 KB against 88.5 KB gzip), their effect on the vitals they
  report, fidelity in Grafana, adapter size. Then two runs and the numbers.

## Order of work

1. ADR 0036 and 0037 accepted; dependencies approved.
2. **Spike first**: `otel-lgtm` with the Faro receiver on, one Faro payload end to
   end. The second adapter depends on it and it is the analysis's largest unknown.
3. A — the switch, the collector config, the fan-out, exemplars, C4.
4. B — the port, the OTel adapter, Noop, the contract suite, the same-origin path.
5. D — the tracking plan, its generated types and its test; the audit.
6. C — the meters and the pulse dashboard.
7. E — the simulator.
8. B' — the Faro adapter, then F.
9. Tags, articles 05 and 06, chapter 22 filed (`material-map.md`, `chapters/en/22-seeing-it-run.md`).

## Non-goals

- **Product analytics storage and funnels** (the outbox → `analytics` schema, the
  first-party client endpoint, the human/agent funnel): specified with phase 11,
  where the agent checkout exists. This work emits the events; it does not store
  them as analytics.
- **Feature flags and experiments**: a phase and an ADR of their own, after this
  one — OpenFeature as an adapter behind `IFeatureGate`, providers compared, the
  port amended for variants (ADR 0023).
- Session replay, alerting to a pager, long retention, and running any of this
  anywhere but a developer machine.
- The AI observability panel and `AiInvocations`: phase 12, unchanged.

## Measures

- Build clean with `-warnaserror`; architecture tests green.
- The degradation tests: collector down (backend), adapter failure (both apps).
- The adapter contract suite passes for OTel and for Noop, later for Faro.
- The tracking-plan test, verified by adding an unplanned event and watching it
  fail (listed in the commit, per the mutation convention).
- `tools/c4.mjs` passes with the new resources.
- Recorded numbers: bundle cost per adapter as built by Angular, memory per
  simulated browser session, and the audit's count of features with and without
  an answer to the question.

## Open questions

- Does the contrib Faro receiver in `otel-lgtm` accept the Faro Web SDK's payload
  unchanged? (The spike answers it; Alloy is the fallback, by amendment.)
- Tracking plan as Markdown held by a test, or YAML that generates both the types
  and the documentation?
- Where the same-origin `/otlp` proxy lives: the API, or a small YARP route in
  front of both apps.
- How much of `https-observability` is worth mirroring for `http`.
