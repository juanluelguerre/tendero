# shared-telemetry

Browser telemetry behind a port (ADR 0036). The apps inject `Telemetry` and call
`trackEvent`, `recordError`, `withSpan`; `provideTelemetry({ service })` asks
`/api/telemetry/config` which adapter to load — OpenTelemetry JS today, Grafana
Faro later, or none — and loads only that one, after the page is up. Every
adapter posts to the page's own `/otlp`, which the dev server proxies to the
Aspire dashboard or to the collector of the full observability mode.

Telemetry never breaks the shop: until an adapter loads, and after one fails,
the no-op is in use, and the tests say so.

`npx nx test shared-telemetry`
