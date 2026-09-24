import { SpanStatusCode } from '@opentelemetry/api';
import { InMemoryLogRecordExporter } from '@opentelemetry/sdk-logs';
import { InMemorySpanExporter } from '@opentelemetry/sdk-trace-web';
import { describeTelemetryAdapterContract, type Emitted } from './adapter-contract';
import { createOtelAdapter } from './otel-adapter';

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
