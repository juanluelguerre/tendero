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

Measured on 2026-09-25 (OTel) and 2026-09-26 (Faro). Both runs: seed 20260925,
40 sessions, 0 failed, 34 of them browser sessions; the stack restarted between
them. Faro was run with its defaults, as declared, except console capture.

| # | Measure | OTel | Faro | Hypothesis |
|---|---|---|---|---|
| 1 | What the adapter downloads, gzip | **36.4 KB** (33.3 KB + 3.1 KB of `web-vitals`, loaded later) | **67.9 KB** | ≥ 2× — **not met**: 1.87× |
| 2 | Telemetry requests per session | **2.6** | **8.4** | Faro sends more — **met**: 3.2× |
| 2 | Telemetry bytes per session | **3.1 KB** | **30.1 KB** | — **9.7×** |
| 3 | LCP p75 | 231 ms | 244 ms | < 10 % apart — **met** (+5.6 %) |
| 3 | INP p75 | 24 ms | 24 ms | **met** |
| 3 | CLS p75 | 0.167 | 0.167 | **met** |
| 4 | Browser traces joined to the API | 266 of 272 (97.8 %) | 206 of 211 (97.6 %) | both join — **met** |
| 4 | Our events / exceptions reaching Loki | 8 / 0 | 8 / 0 | **met** |
| 4 | Web Vitals lines | 152 | 262 measurements | — |
| 4 | Lines carrying the route template | 160 of 160 | 8 (ours only) | — |
| 4 | Lines the SDK sent by itself | — | **1 037** events (211 of them one per request) | Faro adds sessions and views — **met, and more** |
| 5 | Our adapter, lines of code | 137 | 106 as run; **138** once it scrubs URLs | smaller — **met as run, gone once it scrubs** |

**Bundle size** comes from Angular's own build (`stats.json`). Each adapter's
chunk is counted together with every chunk it imports that the initial bundle
does not already load. Measured that way, 1.87× is closer to the esbuild table's
2.2× than the single named chunks suggested (11.0 KB against 42.3 KB): both
adapters share the OpenTelemetry trace SDK, and Angular put it in a chunk of its
own.

**What the comparison found that it did not set out to measure: Faro sends what
the shopper typed.** Faro's own fetch events carry `url.full` as the request had
it, so 170 of its lines held a query string — among them `/api/search?q=mochila`
and `q=lampara que no ocupe sitio en la mesa`, the words a person typed into the
search box. They are *technical* telemetry, so the consent that holds back
behavioural events does not hold these back. The OTel adapter strips query strings
and ids from `url.full` (`scrub-url.ts`, `P9b-4`), and the spec requires it
(section B). The measurement above is of Faro's defaults, as declared; the Faro
adapter now scrubs the same way, in a `beforeSend` hook with a test (`P9b-8`) —
and that hook is 32 lines. The fifth hypothesis held for Faro as it comes, and
not for Faro as this shop has to run it: once it respects the same rule, our
adapter is 138 lines against OTel's 137. Configured, not assembled, until the
configuration has to take something away.

**What it means for Tendero.** Faro buys a richer picture for no extra code of
ours: sessions, page views, one event per request, and resource timing. It costs
twice the download and ten times the bytes on every visit, and it needs a
receiver the default mode does not have. The vitals it reports are the same.
**OTel stays the default; Faro stays an adapter that can be switched on**
(`Parameters:browser-telemetry=faro`) — which is what the port is for.

**Caveats.** One run each, on one laptop, a day apart, with the Faro run on
three temporarily moved ports (Windows had reserved the usual ones). The vitals
are from headless Chromium on the same machine as the whole stack, so they
compare the two adapters with each other, not with real devices.
