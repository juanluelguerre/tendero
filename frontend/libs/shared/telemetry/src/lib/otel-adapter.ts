import { SpanStatusCode, type Span, type Tracer } from '@opentelemetry/api';
import { SeverityNumber, type Logger } from '@opentelemetry/api-logs';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchLogRecordProcessor, LoggerProvider, type LogRecordExporter } from '@opentelemetry/sdk-logs';
import { BatchSpanProcessor, WebTracerProvider, type ReadableSpan, type SpanExporter } from '@opentelemetry/sdk-trace-web';
import { scrubUrl } from './scrub-url';
import { isSynthetic } from './synthetic';
import type { ActorKind, TelemetryAdapter, TelemetryContext } from './telemetry';

/** What the tests replace: where spans and log records go, and whether Web Vitals are observed. */
export interface OtelAdapterOptions {
  readonly spanExporter?: SpanExporter;
  readonly logExporter?: LogRecordExporter;
  readonly webVitals?: boolean;

  /**
   * Off only in unit tests: under vitest the instrumentation package resolves its
   * Node build, which hooks `require` and fails outside Node's module loader, and
   * jsdom has no network worth tracing anyway. The browser path is proved in a
   * real browser against the running stack.
   */
  readonly instrumentFetch?: boolean;
}

type Attributes = Readonly<Record<string, string | number | boolean>>;

/**
 * The first adapter (ADR 0036): OpenTelemetry JS, exporting OTLP as protobuf to
 * the page's own `/otlp`.
 *
 * Protobuf and not JSON because the Aspire dashboard's OTLP/HTTP endpoint reads
 * only protobuf, and in the default mode the dev server's proxy hands the
 * browser's requests straight to it. The collector of the full mode reads both.
 *
 * **What it instruments by itself**: every `fetch` — which is every API call,
 * because both apps use `HttpClient` with `withFetch()` — as a client span whose
 * `traceparent` joins the API's trace; and Core Web Vitals. **What it is told**:
 * events, errors, spans of our own and the route, through the port. Events,
 * errors and vitals are log records with an event name, which is how
 * OpenTelemetry carries events now that its separate events API is gone.
 *
 * Angular 22 is zoneless, so the context manager is the SDK's default stack
 * manager and nothing depends on zone.js; the instrumentation that did — user
 * interaction — is not used.
 */
export function createOtelAdapter(options: OtelAdapterOptions = {}): TelemetryAdapter {
  let tracer: Tracer;
  let logger: Logger;
  let flushAll: () => Promise<void> = async () => undefined;
  let currentRoute = '';
  let session = '';

  const withRoute = (attributes: Attributes): Attributes =>
    currentRoute ? { ...attributes, 'url.template': currentRoute, 'session.id': session } : { ...attributes, 'session.id': session };

  return {
    name: 'otel',

    start(context: TelemetryContext): void {
      session = context.sessionId;

      const resource = resourceFromAttributes({
        'service.name': context.service,
        'service.namespace': 'tendero',
        // The traffic simulator's browsers say so in their User-Agent (ADR 0037);
        // everything this page sends then carries OpenTelemetry's own label.
        ...(isSynthetic() ? { 'user_agent.synthetic.type': 'test' } : {}),
      });

      const tracerProvider = new WebTracerProvider({
        resource,
        spanProcessors: [
          new BatchSpanProcessor(options.spanExporter ?? new OTLPTraceExporter({ url: `${context.endpoint}/v1/traces` })),
        ],
      });

      // Registers the W3C propagator too, which is what puts `traceparent` on
      // the API requests; same-origin requests carry it without being listed.
      tracerProvider.register();

      if (options.instrumentFetch !== false) instrumentFetch(tracerProvider, context.endpoint, () => session);

      const loggerProvider = new LoggerProvider({
        resource,
        processors: [
          // An options object, unlike the span processor beside it: handed the
          // exporter directly it compiles in plain JavaScript, keeps no exporter
          // and drops every record without a word.
          new BatchLogRecordProcessor({
            exporter: options.logExporter ?? new OTLPLogExporter({ url: `${context.endpoint}/v1/logs` }),
          }),
        ],
      });

      flushAll = async () => {
        await tracerProvider.forceFlush();
        await loggerProvider.forceFlush();
      };

      tracer = tracerProvider.getTracer('tendero.browser');
      logger = loggerProvider.getLogger('tendero.browser');

      // A batch still waiting when the tab goes away is sent then, or never.
      addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') void flushAll();
      });

      if (options.webVitals !== false) void observeWebVitals((name, value, rating) =>
        logger.emit({
          eventName: 'browser.web_vital',
          severityNumber: SeverityNumber.INFO,
          attributes: withRoute({ 'web_vital.name': name, 'web_vital.value': value, 'web_vital.rating': rating }),
        }),
      );
    },

    event(name: string, attributes: Attributes, actor: ActorKind): void {
      logger.emit({
        eventName: name,
        severityNumber: SeverityNumber.INFO,
        attributes: withRoute({ ...attributes, 'actor.kind': actor }),
      });
    },

    error(error: unknown): void {
      const exception = error instanceof Error ? error : new Error(String(error));
      logger.emit({
        eventName: 'exception',
        severityNumber: SeverityNumber.ERROR,
        attributes: withRoute({
          'exception.type': exception.name,
          'exception.message': exception.message,
          'exception.stacktrace': exception.stack ?? '',
        }),
      });
    },

    span<T>(name: string, attributes: Attributes, work: () => T): T {
      return tracer.startActiveSpan(name, { attributes: withRoute(attributes) }, (span) => {
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
    },

    flush: () => flushAll(),
  };
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

/** LCP, INP and CLS, each reported when the browser settles it. Loaded only with this adapter. */
async function observeWebVitals(report: (name: string, value: number, rating: string) => void): Promise<void> {
  const { onCLS, onINP, onLCP } = await import('web-vitals');
  for (const observe of [onCLS, onINP, onLCP]) observe((metric) => report(metric.name, metric.value, metric.rating));
}


/**
 * Every `fetch` as a client span — which is every API call, because both apps use
 * `HttpClient` with `withFetch()` — carrying `traceparent` into the API's trace.
 */
function instrumentFetch(provider: WebTracerProvider, endpoint: string, session: () => string): void {
  const instrumentation = new FetchInstrumentation({
    // The exporter's own requests are not traced: a trace of every export is an
    // export of every trace.
    ignoreUrls: [new RegExp(`${escape(endpoint)}/`)],
    applyCustomAttributesOnSpan: (span) => {
      span.setAttribute('session.id', session());
      const recorded = (span as unknown as ReadableSpan).attributes?.['url.full'];
      if (typeof recorded === 'string') span.setAttribute('url.full', scrubUrl(recorded));
    },
  });
  instrumentation.setTracerProvider(provider);
}
