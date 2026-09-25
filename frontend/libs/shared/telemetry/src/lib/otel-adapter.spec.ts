import { SpanStatusCode } from '@opentelemetry/api';
import { InMemoryLogRecordExporter } from '@opentelemetry/sdk-logs';
import { InMemorySpanExporter } from '@opentelemetry/sdk-trace-web';
import { describeTelemetryAdapterContract, type Emitted } from './adapter-contract';
import { createOtelAdapter, SYNTHETIC_USER_AGENT } from './otel-adapter';

describeTelemetryAdapterContract('The OpenTelemetry adapter', () => {
  const spans = new InMemorySpanExporter();
  const logs = new InMemoryLogRecordExporter();
  const adapter = createOtelAdapter({ spanExporter: spans, logExporter: logs, webVitals: false, instrumentFetch: false });

  return {
    adapter,
    async emitted(): Promise<readonly Emitted[]> {
      await adapter.flush?.();
      return [
        ...logs.getFinishedLogRecords().map((record) => ({
          kind: 'event' as const,
          name: record.eventName ?? '',
          attributes: record.attributes,
        })),
        ...spans.getFinishedSpans().map((span) => ({
          kind: 'span' as const,
          name: span.name,
          attributes: span.attributes,
          failed: span.status.code === SpanStatusCode.ERROR,
        })),
      ];
    },
  };
});

describe('The OpenTelemetry adapter and simulated traffic', () => {
  afterEach(() => vi.restoreAllMocks());

  async function resourceOfASpan(): Promise<Readonly<Record<string, unknown>>> {
    const spans = new InMemorySpanExporter();
    const adapter = createOtelAdapter({
      spanExporter: spans,
      logExporter: new InMemoryLogRecordExporter(),
      webVitals: false,
      instrumentFetch: false,
    });
    adapter.start({ service: 'storefront', sessionId: 's', endpoint: '/otlp' });
    adapter.span('work', {}, () => undefined);
    await adapter.flush?.();
    return spans.getFinishedSpans()[0].resource.attributes;
  }

  it('labels everything a simulated browser sends as synthetic (ADR 0037)', async () => {
    vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue(`Mozilla/5.0 HeadlessChrome ${SYNTHETIC_USER_AGENT}1.0 (synthetic)`);

    expect((await resourceOfASpan())['user_agent.synthetic.type']).toBe('test');
  });

  it('labels nothing for a person', async () => {
    vi.spyOn(navigator, 'userAgent', 'get').mockReturnValue('Mozilla/5.0 Chrome');

    expect((await resourceOfASpan())['user_agent.synthetic.type']).toBeUndefined();
  });
});
