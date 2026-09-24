import type { TelemetryAdapter, TelemetryContext } from './telemetry';

/** One thing an adapter sent, in a shape every adapter can be read back into. */
export interface Emitted {
  readonly kind: 'event' | 'span';
  readonly name: string;
  readonly attributes: Readonly<Record<string, unknown>>;
  readonly failed?: boolean;
}

export interface AdapterHarness {
  readonly adapter: TelemetryAdapter;
  /** Everything the adapter has sent so far, after flushing its batches. */
  emitted(): Promise<readonly Emitted[]>;
}

const context: TelemetryContext = { service: 'storefront', sessionId: 'session-1', endpoint: '/otlp' };

/**
 * What every browser telemetry adapter must do, whatever SDK is under it
 * (ADR 0036). OpenTelemetry passes it today; Faro will pass the same suite, and
 * the comparison between them is only fair because of that. Written once, run
 * per adapter — the `CatalogSourceConnectorContractTests` pattern, in TypeScript.
 */
export function describeTelemetryAdapterContract(name: string, harness: () => AdapterHarness): void {
  describe(`${name} honours the telemetry port`, () => {
    let subject: AdapterHarness;

    beforeEach(() => {
      subject = harness();
      subject.adapter.start(context);
    });

    it('sends an event with its name, its attributes, who acted and the session', async () => {
      subject.adapter.event('webmcp_tool_invoked', { tool: 'search_products', outcome: 'answered' }, 'webmcp');

      const [event] = await subject.emitted();
      expect(event).toMatchObject({
        kind: 'event',
        name: 'webmcp_tool_invoked',
        attributes: {
          tool: 'search_products',
          outcome: 'answered',
          'actor.kind': 'webmcp',
          'session.id': 'session-1',
        },
      });
    });

    it('stamps the route template on what it sends after a navigation', async () => {
      subject.adapter.route('/:lang/p/:code');
      subject.adapter.event('webmcp_tool_invoked', { tool: 'get_cart', outcome: 'answered' }, 'human');

      const [event] = await subject.emitted();
      expect(event?.attributes['url.template']).toBe('/:lang/p/:code');
    });

    it('sends an error as an exception, with its type and message', async () => {
      subject.adapter.error(new TypeError('price is undefined'));

      const [error] = await subject.emitted();
      expect(error).toMatchObject({
        name: 'exception',
        attributes: { 'exception.type': 'TypeError', 'exception.message': 'price is undefined' },
      });
    });

    it('wraps work in a span and hands back what the work returned', async () => {
      expect(subject.adapter.span('checkout', { step: 'pay' }, () => 'paid')).toBe('paid');

      const [span] = await subject.emitted();
      expect(span).toMatchObject({ kind: 'span', name: 'checkout', attributes: { step: 'pay' } });
      expect(span?.failed).toBeFalsy();
    });

    it('ends an async span when the promise settles, and marks a rejection as failed', async () => {
      await expect(
        subject.adapter.span('checkout', {}, () => Promise.reject(new Error('card declined'))),
      ).rejects.toThrow('card declined');

      const [span] = await subject.emitted();
      expect(span).toMatchObject({ kind: 'span', name: 'checkout', failed: true });
    });
  });
}
