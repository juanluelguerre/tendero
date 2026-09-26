/**
 * The dev server forwards /api to the backend. Aspire injects the URL as
 * `services__api__http__0` when started with `dotnet run --project
 * src/AppHost`; the fallback value only matters if somebody brings the frontend
 * up on its own. It is the ONLY place in the repo where a backend localhost
 * appears.
 */
const api =
  process.env['services__api__http__0'] ??
  process.env['services__api__https__0'] ??
  'http://localhost:5130';

/**
 * /otlp: the browser's telemetry (ADR 0036), posted to the page's own origin and
 * forwarded from here. Aspire gives this dev server the same OTLP address and key
 * it gives every resource — the dashboard by default, the collector of the full
 * observability mode when Tendero__Observability is on — so the browser never
 * learns either: no CORS, nothing for an ad blocker to match, and no dashboard
 * key in the page. Started without Aspire there is no address, no route, and the
 * exporter's requests fail quietly.
 */
const otlp = process.env['OTEL_EXPORTER_OTLP_ENDPOINT'];
const otlpHeaders = Object.fromEntries(
  (process.env['OTEL_EXPORTER_OTLP_HEADERS'] ?? '')
    .split(',')
    .filter((header) => header.includes('='))
    .map((header) => [header.slice(0, header.indexOf('=')).trim(), header.slice(header.indexOf('=') + 1).trim()])
    .filter(([name, value]) => name && value),
);

/**
 * /faro: the second browser adapter's payload (P9b-8), forwarded to the
 * collector's Faro receiver — which exists only in the full observability mode,
 * so the variable, and the route, exist only there too.
 */
const faro = process.env['FARO_ENDPOINT'];

const telemetry = otlp
  ? [
      {
        context: ['/otlp'],
        target: otlp,
        secure: false,
        changeOrigin: true,
        headers: otlpHeaders,
        pathRewrite: { '^/otlp': '' },
      },
    ]
  : [];

if (faro) {
  telemetry.push({ context: ['/faro'], target: faro, secure: false, changeOrigin: true });
}

export default [
  {
    context: ['/api'],
    target: api,
    secure: false,
    changeOrigin: true,
  },
  ...telemetry,
];
