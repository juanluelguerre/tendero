# OpenTelemetry JS against Grafana Faro — the declared comparison

**Declared on 2026-09-25, before the Faro adapter existed and before either run**
(ADR 0036, decision 6; the method ADR 0033 used). What follows the *Results*
heading is written after the runs, and nothing above it is edited once they
start: a hypothesis adjusted to its result is not a hypothesis.

## The question

Tendero's browser telemetry is a port with two real adapters. OpenTelemetry JS is
the standard, and assembled by hand; Grafana Faro is a product, and brings more
by itself. **Is what Faro adds worth what it costs, for this shop?**

## Hypotheses

1. **Bundle.** Faro's lazy chunk, as Angular builds it, is at least **twice**
   OTel's. The measured table (`observability-tooling.md` §2, esbuild) gives
   39.7 KB against 88.5 KB gzip for like-for-like setups; Angular's tree-shaking
   may move both.
2. **Network.** Faro sends **more** per session — requests and bytes — because it
   collects more by itself (session, page views, its own metadata).
3. **Web Vitals.** Neither adapter moves the vitals it reports: both load after
   the first render, never waited for. The p75 of LCP, INP and CLS differs by
   **less than 10 %** between the runs.
4. **Fidelity.** Both join the browser's request to the API's span (one trace),
   and both deliver events, errors and vitals to Loki with the route template.
   Faro additionally delivers a session and page views without being asked.
5. **Our code.** The Faro adapter is **smaller** than the OTel one: Faro is
   configured, OTel is assembled.

## The setup

- The full observability mode (`https-observability`): the collector with the
  Faro receiver, Tempo, Loki, Prometheus. Faro reaches the receiver through the
  dev server's same-origin `/faro` path, as OTel reaches `/otlp`.
- The adapter is chosen by the API's `Telemetry:Browser:Adapter` — the only
  thing that differs between the runs.
- **The same traffic**: `tools/TrafficSim` with seed **20260925**, **40 sessions**,
  a simulated day of ten minutes starting at 20:00, up to six browsers. Same seed,
  same sessions, same steps (ADR 0037); the shop's answers may differ in timing,
  never in what the sessions do.
- One run per adapter, on the same machine, one after the other, the stack
  restarted between them.

## What is measured, and how

| # | Measure | How |
|---|---|---|
| 1 | Lazy chunk, gzip | `nx build storefront`, the adapter's chunk in Angular's own output |
| 2 | Requests and bytes to the telemetry path, per session | the simulator counts what each page posts to `/otlp` or `/faro` and tags the session span |
| 3 | p75 LCP, INP, CLS | Loki, over each run's window, as the *Shop pulse* computes them |
| 4 | Browser fetch spans joined to an API span | Tempo: browser spans whose trace also holds an `api` span, over all browser fetch spans |
| 4 | Events, errors, vitals in Loki with the route template | Loki, per kind |
| 5 | Adapter size | lines of our adapter file, comments and blank lines excluded |

## Results

*To be written after the runs.*
