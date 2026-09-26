# ADR 0036 — Observability is a second stack behind a switch, browser telemetry is a port, and every feature names the event that proves it

Status: accepted · 2026-09-24 · Related: [ADR 0017](0017-the-identity-provider-is-a-port.md), [ADR 0023](0023-feature-flags-with-removal-conditions.md), [ADR 0033](0033-a-design-artefact-is-disposable.md), [ADR 0035](0035-the-architecture-is-drawn-from-models-held-to-the-code.md) · Analysis: [`observability-tooling.md`](../analysis/observability-tooling.md)

## Context

Tendero has exported OpenTelemetry since its first slice, and CLAUDE.md asks for
an `ActivitySource` in every slice with I/O. What it has is uneven: spans in
nearly every slice, custom metrics in two files, and **nothing from either
frontend**. Everything lands in the Aspire dashboard, which keeps its data in
memory and has no dashboards of its own, no history and no SQL. Nobody can see the
shop run — a person's click joined to the span it caused in Ordering, a checkout
funnel, the share of traffic that is an agent — and phases 10 and 11 build exactly
the behaviour most worth seeing: claims approved by a person, and an agent checkout
that gets refused.

`P12-1` put the Grafana stack in the phase of the copilot. It is the substrate of
the fourth and sixth differentiators, not an appendix to one of them, and it is
brought forward to before `P10-1`.

## Decision

**1 · Two modes, and Aspire is never replaced.** The default is today's: services
and browsers export to the Aspire dashboard. `Tendero__Observability=true` (a
launch profile, `https-observability`) adds `grafana/otel-lgtm` — OTel Collector
contrib, Prometheus, Tempo, Loki, Grafana — and points every exporter at its
collector, which fans out to **both** the Aspire dashboard and the Grafana stack.
The resource is always declared, `WithExplicitStart()` when the switch is off, and
wired only when it is on: the pattern of `Tendero:Vectors`. It is a start-time
switch because an exporter's endpoint is fixed when the AppHost starts the
service. The images are tier 2 (Apache-2.0 and AGPLv3, unmodified); our
`otelcol-config.yaml` narrows CORS to the two app origins and enables the contrib
Faro receiver.

**2 · Browser telemetry is a port.** `libs/shared/telemetry` exposes what our code
emits — typed events, errors, the actor kind, explicit spans, consent — and each
adapter owns its automatic instrumentation and transport. Adapters:
`OtelTelemetryProvider` first, `FaroTelemetryProvider` second, `NoopTelemetryProvider`
for tests, missing consent and a failed start. The adapter is chosen at runtime
from configuration and loaded with a dynamic `import()`; **exactly one runs per
page load**, because both patch `fetch` and a second global registration fails.
Telemetry never breaks the shop: a failed start is Noop, and the degradation is
tested. The browser sends to a same-origin `/otlp/v1/*`, which removes ad-blocker
and CORS problems together and hides the mode from the frontends.

**3 · A port of our own where no standard reaches, the standard where one does.**
The backend needs no port: OpenTelemetry *is* the abstraction there. The browser
does, because OTel's browser instrumentation is experimental and Faro's event API
is not OTel's. Flags have a port already (ADR 0023) and keep it.

**4 · Every feature names the event that proves it.** *Which event proves it is
used, and that it is worth it?* A versioned tracking plan lists each event with its
schema, the question it answers and the decision its value would change; an event
with no decision does not enter. The frontend's event names are generated from the
plan, so an unplanned event does not compile, and a test holds the backend to it.
The definition of done gains the line, and the spec template gains the section.

**5 · The split between a person and an agent is computed on the server.** Every
metric, span and analytics row carries `actor.kind` (human / agent) and the
surface (web / webmcp / mcp / ucp), taken from the authenticated principal
(invariant 10) — the one place that knows. No label that identifies a user, a
product or an order goes to Prometheus.

**6 · A declared comparison.** Both browser adapters run against the same seeded
traffic (ADR 0037) in separate runs. The hypothesis and what is measured — bundle
cost, each SDK's own effect on the Web Vitals it reports, what reaches Grafana,
the adapter's size — are written before the first run, as ADR 0033 did.

## Consequences

- New npm dependencies for the first adapter, **approved by the author on
  2026-09-24**, tier 1, each licence checked at the exact version added — all
  Apache-2.0, and every transitive package Apache-2.0 or MIT: `@opentelemetry/api`
  1.9.1, `api-logs`, `sdk-logs`, `instrumentation-fetch`,
  `exporter-trace-otlp-proto` and `exporter-logs-otlp-proto` 0.222.0,
  `sdk-trace-web` and `resources` 2.11.0, and `web-vitals` 6.2.2. The list that
  was approved changed three ways while building (`P9b-4`): the exporters are the
  **protobuf** ones, because the Aspire dashboard's OTLP/HTTP endpoint reads only
  protobuf; `instrumentation-xml-http-request` and `sdk-trace-base` went, because
  both apps call the API through `fetch` and `sdk-trace-web` re-exports what was
  needed from the other; and `resources` and the logs exporter came in, for the
  service name and for events. The adapter is a lazy chunk of 26 KB gzip; the
  initial bundle grew by 0.8 KB.
- The browser learns its adapter from `GET /api/telemetry/config`, anonymous, the
  way it learns its issuer from `/api/auth/config`: one value, owned by the
  server, that two apps cannot disagree about. Faro's were a
  separate approval, **given by the author on 2026-09-25** (`P9b-8`):
  `@grafana/faro-web-sdk` and `@grafana/faro-web-tracing` 2.12.1, tier 1,
  Apache-2.0, bringing six packages in all — `faro-core` and two OpenTelemetry
  packages (Apache-2.0) and `ua-parser-js` **1.0.41** (MIT). Faro's clean tree
  rests on that exact pin: `ua-parser-js` 2.x is AGPL-3.0, so it is checked on
  every Faro upgrade. The Faro adapter reaches a receiver only in the full
  observability mode; the Aspire dashboard reads OTLP, not Faro's payload.
- The instrumentation that depends on zone.js is not used: Angular 22 is zoneless,
  and a user action is joined to its request explicitly, in an interceptor.
- The C4 model gains the stack as containers (ADR 0035), and `tools/c4.mjs` names
  the switch.
- `P12-1` is done here; phase 12 keeps the AI panel and `AiInvocations`.
- Session replay is out: it needs prior consent and there is no real behaviour to
  replay. Product analytics is first-party and is specified with phase 11.

## Amendment · 2026-09-25 (`P9b-6`)

**WebMCP is not a surface the server can see, and the metrics do not pretend
otherwise.** Decision 5 listed four surfaces — web, webmcp, mcp, ucp. Building it
showed the server can tell only three apart: WebMCP runs in the shopper's own tab
and **inherits their session** (the three trust models in CLAUDE.md), so its
requests are the web's, by design. A header from the browser could label them,
and it would be a client asserting a fact the server then reports as its own —
the one thing decision 5 was written to stop. So on the server `surface` is
`web | mcp | ucp`, read from the route, and `actor.kind` is `agent` for MCP and
UCP (both anonymous by decision, so the principal alone would call every external
agent a person) and for a principal carrying an agent id. WebMCP's evidence is the
browser's own `webmcp_tool_invoked`, which already existed.

**Consent is in the port, as decision 2 said, and `P9b-4` had left it out.** The
tracking plan marks each event *behavioural* or *technical*; the facade drops a
behavioural event until the shopper has said yes, whatever the adapter; errors,
Web Vitals and spans flow either way. The storefront asks once, at the foot of the
page, with yes and no the same size.

