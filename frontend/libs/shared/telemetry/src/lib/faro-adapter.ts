import {
  getWebInstrumentations,
  initializeFaro,
  type BrowserConfig,
  type Faro,
  type Transport,
  type TransportItem,
} from '@grafana/faro-web-sdk';
import { TracingInstrumentation } from '@grafana/faro-web-tracing';
import { SpanStatusCode, type Span } from '@opentelemetry/api';
import { scrubUrl } from './scrub-url';
import { isSynthetic } from './synthetic';
import type { ActorKind, TelemetryAdapter, TelemetryContext } from './telemetry';

/** Where the page posts Faro's payload, on its own origin; the dev server forwards it to the collector's Faro receiver. */
export const FARO_ENDPOINT = '/faro';

/**
 * The domain our events carry, beside Faro's own (`browser`: sessions, views,
 * navigation). It is how a Loki query — or a test — tells what the shop said
 * from what the SDK collected by itself.
 */
export const EVENT_DOMAIN = 'tendero';

/** What the tests replace: where items go, and whether they wait to be batched. */
export interface FaroAdapterOptions {
  readonly transports?: Transport[];
  readonly batching?: BrowserConfig['batching'];

  /** Off only in unit tests, for the reason `OtelAdapterOptions.instrumentFetch` gives. */
  readonly instrumentFetch?: boolean;
}

type Attributes = Readonly<Record<string, string | number | boolean>>;

/**
 * The second adapter (ADR 0036): Grafana Faro, posting its own payload to the
 * page's `/faro`, which the collector's Faro receiver turns into the same Loki
 * lines and Tempo traces the first adapter produces.
 *
 * **Configured, not assembled** — which is the comparison's fifth hypothesis
 * (`docs/analysis/otel-vs-faro.md`). Faro's defaults are kept, because judging a
 * product by switching its product off would not be a fair comparison: errors,
 * Web Vitals, sessions, page views and resource timing arrive by themselves.
 * Console capture is the one default turned off — a console line can carry
 * anything, and nothing here decided it should leave the browser.
 *
 * Tracing is Faro's own OpenTelemetry (`faro-web-tracing`): its fetch
 * instrumentation carries `traceparent` into the API's trace, as the OTel
 * adapter's does, and it registers with the same OpenTelemetry API — which is why
 * exactly one adapter runs per page load.
 *
 * Faro's event attributes are strings, so ours are sent as strings; the
 * receiver writes them to Loki as text either way.
 */
export function createFaroAdapter(options: FaroAdapterOptions = {}): TelemetryAdapter {
  let faro: Faro;
  let currentRoute = '';
  let session = '';

  const withRoute = (attributes: Attributes): Record<string, string> => {
    const text = Object.fromEntries(Object.entries(attributes).map(([key, value]) => [key, String(value)]));
    return currentRoute ? { ...text, 'url.template': currentRoute, 'session.id': session } : { ...text, 'session.id': session };
  };

  const tracer = () => faro.api.getOTEL()?.trace.getTracer('tendero.browser');

  return {
    name: 'faro',

    start(context: TelemetryContext): void {
      session = context.sessionId;
      const origin = typeof location === 'undefined' ? 'http://localhost' : location.origin;

      faro = initializeFaro({
        url: options.transports ? undefined : `${origin}${FARO_ENDPOINT}`,
        transports: options.transports,
        batching: options.batching,
        // Its own instance, not the global: a page runs one adapter, a test
        // suite runs many.
        isolate: true,
        beforeSend: scrubItem,
        app: {
          name: context.service,
          namespace: 'tendero',
        },
        // The traffic simulator's browsers say so (ADR 0037), in the same
        // attribute the OTel adapter puts on its resource.
        sessionTracking: isSynthetic() ? { session: { attributes: { 'user_agent.synthetic.type': 'test' } } } : undefined,
        instrumentations: [
          ...getWebInstrumentations({ captureConsole: false }),
          new TracingInstrumentation({
            instrumentations: options.instrumentFetch === false ? [] : undefined,
            instrumentationOptions: {
              // Same origin only: the API is behind the dev server's /api.
              propagateTraceHeaderCorsUrls: [new RegExp(`^${escape(origin)}/api/`)],
            },
          }),
        ],
      });
    },

    event(name: string, attributes: Attributes, actor: ActorKind): void {
      faro.api.pushEvent(name, withRoute({ ...attributes, 'actor.kind': actor }), EVENT_DOMAIN);
    },

    error(error: unknown): void {
      const exception = error instanceof Error ? error : new Error(String(error));
      faro.api.pushError(exception, { context: withRoute({}) });
    },

    span<T>(name: string, attributes: Attributes, work: () => T): T {
      const otel = tracer();
      if (!otel) return work();

      return otel.startActiveSpan(name, { attributes: withRoute(attributes) }, (span) => {
        try {
          const result = work();
          if (result instanceof Promise) {
            return result.then(
              (value) => end(span, value),
              (error: unknown) => fail(span, error),
            ) as T;
          }
          return end(span, result);
        } catch (error) {
          return fail(span, error);
        }
      });
    },

    route(template: string): void {
      currentRoute = template;
      faro.api.setView({ name: template });
    },

    /** Faro's spans wait in a batch processor; its items in the transport's batch, which the tests switch off. */
    async flush(): Promise<void> {
      const provider = faro.api.getOTEL()?.trace.getTracerProvider() as unknown as Flushable | undefined;
      await (provider?.getDelegate?.() ?? provider)?.forceFlush?.();
    },
  };
}

/**
 * Every URL in what Faro sends, without its query string and with its ids
 * masked — the rule the OTel adapter already follows (`scrub-url.ts`) and the
 * spec asks of both (section B).
 *
 * Found by the declared comparison (`docs/analysis/otel-vs-faro.md`): Faro's own
 * fetch events and spans carry `url.full` as the request had it, and the search
 * endpoint's has the words the shopper typed — 170 such lines in one run of 34
 * sessions. They are technical telemetry, so consent does not stop them; this
 * does.
 */
export function scrubItem(item: TransportItem): TransportItem {
  const payload = item.payload as unknown as Record<string, unknown>;

  const attributes = payload['attributes'];
  if (attributes && typeof attributes === 'object') {
    const scrubbed = attributes as Record<string, unknown>;
    for (const key of URL_KEYS) {
      if (typeof scrubbed[key] === 'string') scrubbed[key] = scrubUrl(scrubbed[key]);
    }
  }

  for (const span of spansOf(payload)) {
    for (const attribute of span.attributes ?? []) {
      if (URL_KEYS.includes(attribute.key) && typeof attribute.value?.stringValue === 'string') {
        attribute.value.stringValue = scrubUrl(attribute.value.stringValue);
      }
    }
  }

  return item;
}

/** The attributes a URL travels in: OpenTelemetry's current name, and its older one. */
const URL_KEYS = ['url.full', 'http.url'];

interface OtlpSpanLike {
  attributes?: { key: string; value?: { stringValue?: string } }[];
}

function spansOf(payload: Record<string, unknown>): OtlpSpanLike[] {
  const resourceSpans = payload['resourceSpans'];
  if (!Array.isArray(resourceSpans)) return [];
  return resourceSpans
    .flatMap((resource: { scopeSpans?: { spans?: OtlpSpanLike[] }[] }) => resource.scopeSpans ?? [])
    .flatMap((scope) => scope.spans ?? []);
}

interface Flushable {
  getDelegate?(): Flushable;
  forceFlush?(): Promise<void>;
}

function end<T>(span: Span, value: T): T {
  span.end();
  return value;
}

function fail(span: Span, error: unknown): never {
  span.recordException(error instanceof Error ? error : String(error));
  span.setStatus({ code: SpanStatusCode.ERROR });
  span.end();
  throw error;
}

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
