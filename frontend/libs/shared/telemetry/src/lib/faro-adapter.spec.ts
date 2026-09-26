import { BaseTransport, type TransportItem } from '@grafana/faro-web-sdk';
import { context, propagation, trace } from '@opentelemetry/api';
import { describeTelemetryAdapterContract, type Emitted } from './adapter-contract';
import { createFaroAdapter, EVENT_DOMAIN, scrubItem } from './faro-adapter';

/** Keeps what Faro would have posted, so the suite can read it back. */
class CapturingTransport extends BaseTransport {
  readonly name = 'capturing-transport';
  readonly version = '1.0.0';
  readonly items: TransportItem[] = [];

  send(items: TransportItem | TransportItem[]): void {
    this.items.push(...(Array.isArray(items) ? items : [items]));
  }
}

interface OtlpValue {
  readonly stringValue?: string;
  readonly intValue?: number;
  readonly doubleValue?: number;
  readonly boolValue?: boolean;
}

interface OtlpSpan {
  readonly name: string;
  readonly attributes?: readonly { key: string; value: OtlpValue }[];
  readonly status?: { code?: number };
}

interface TracePayload {
  readonly resourceSpans?: readonly { scopeSpans?: readonly { spans?: readonly OtlpSpan[] }[] }[];
}

/**
 * Faro's items in the shape the contract reads: an event as an event, an
 * exception as the event OpenTelemetry would have called `exception`, and each
 * span of a trace item as a span.
 */
function read(item: TransportItem): Emitted[] {
  const payload = item.payload as unknown as Record<string, unknown>;

  switch (item.type) {
    case 'event':
      // Ours only: Faro's own session and view events are what the comparison
      // counts, not what the port promises.
      return payload['domain'] === EVENT_DOMAIN
        ? [{ kind: 'event', name: String(payload['name']), attributes: (payload['attributes'] as Record<string, unknown>) ?? {} }]
        : [];
    case 'exception':
      return [
        {
          kind: 'event',
          name: 'exception',
          attributes: {
            'exception.type': payload['type'],
            'exception.message': payload['value'],
            ...((payload['context'] as Record<string, unknown>) ?? {}),
          },
        },
      ];
    case 'trace':
      return ((payload as TracePayload).resourceSpans ?? [])
        .flatMap((resource) => resource.scopeSpans ?? [])
        .flatMap((scope) => scope.spans ?? [])
        .map((span) => ({
          kind: 'span' as const,
          name: span.name,
          attributes: Object.fromEntries(
            (span.attributes ?? []).map(({ key, value }) => [
              key,
              value.stringValue ?? value.intValue ?? value.doubleValue ?? value.boolValue,
            ]),
          ),
          failed: span.status?.code === 2,
        }));
    default:
      // Sessions, page views, measurements: Faro's own, and what the
      // comparison counts — not what the port promises.
      return [];
  }
}

// jsdom has no resource timing, which Faro's performance instrumentation reads
// on start; a browser has it. An empty list is what a page with nothing loaded
// yet would answer.
if (typeof performance.getEntriesByType !== 'function') {
  Object.defineProperty(performance, 'getEntriesByType', { value: () => [], configurable: true });
}

describeTelemetryAdapterContract('The Faro adapter', () => {
  // Faro registers its tracer provider with OpenTelemetry's GLOBAL API, which
  // accepts one registration per page — the reason one adapter runs per page
  // load. A suite starts many; each gets the global API to itself.
  trace.disable();
  context.disable();
  propagation.disable();

  const transport = new CapturingTransport();
  const adapter = createFaroAdapter({ transports: [transport], batching: { enabled: false }, instrumentFetch: false });

  return {
    adapter,
    async emitted(): Promise<readonly Emitted[]> {
      await adapter.flush?.();
      return transport.items.flatMap(read);
    },
  };
});

/**
 * What the declared comparison found (docs/analysis/otel-vs-faro.md): Faro's own
 * fetch events and spans carried the search text a shopper typed. Everything
 * leaves without its query string, as the OTel adapter's does.
 */
describe('The Faro adapter scrubs every URL it sends', () => {
  it('in an event: no query string, no id', () => {
    const item = {
      type: 'event',
      payload: {
        name: 'faro.tracing.fetch',
        attributes: { 'url.full': 'http://localhost:4200/api/search?culture=es&q=lampara%20que%20no%20ocupe%20sitio' },
      },
      meta: {},
    } as unknown as TransportItem;

    const attributes = (scrubItem(item).payload as unknown as { attributes: Record<string, string> }).attributes;

    expect(attributes['url.full']).toBe('http://localhost:4200/api/search');
  });

  it('in a span of a trace', () => {
    const item = {
      type: 'trace',
      payload: {
        resourceSpans: [
          {
            scopeSpans: [
              {
                spans: [
                  {
                    name: 'HTTP GET',
                    attributes: [
                      { key: 'url.full', value: { stringValue: 'http://localhost:4200/api/orders/0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b?x=1' } },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
      meta: {},
    } as unknown as TransportItem;

    const span = (scrubItem(item).payload as unknown as TracePayload).resourceSpans?.[0]?.scopeSpans?.[0]?.spans?.[0];

    expect(span?.attributes?.[0]?.value.stringValue).toBe('http://localhost:4200/api/orders/:id');
  });
});

