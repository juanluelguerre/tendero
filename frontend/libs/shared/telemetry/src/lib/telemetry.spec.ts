import { TestBed } from '@angular/core/testing';
import { Telemetry, type TelemetryAdapter, type TelemetryContext } from './telemetry';

const context: TelemetryContext = { service: 'storefront', sessionId: 'abc', endpoint: '/otlp' };

function recording(overrides: Partial<TelemetryAdapter> = {}): TelemetryAdapter & { readonly calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    name: 'recording',
    start: () => undefined,
    event: (name) => void calls.push(`event ${name}`),
    error: () => void calls.push('error'),
    span: (name, _attributes, work) => {
      calls.push(`span ${name}`);
      return work();
    },
    route: (template) => void calls.push(`route ${template}`),
    ...overrides,
  };
}

describe('Telemetry, the facade the apps inject', () => {
  let telemetry: Telemetry;

  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    telemetry = TestBed.inject(Telemetry);
  });

  it('starts with the no-op, so calls before an adapter has loaded are harmless', () => {
    expect(telemetry.adapterName).toBe('noop');
    expect(() => telemetry.trackEvent('webmcp_tool_invoked', { tool: 'search', outcome: 'answered' })).not.toThrow();
    expect(telemetry.withSpan('checkout', () => 42)).toBe(42);
  });

  it('hands every call to the adapter once it is in use, a person unless it says otherwise', () => {
    const actors: string[] = [];
    const adapter = recording({ event: (_name, _attributes, actor) => void actors.push(actor) });
    telemetry.use(adapter, context);

    telemetry.trackEvent('webmcp_tool_invoked', { tool: 'search', outcome: 'answered' });
    telemetry.trackEvent('webmcp_tool_invoked', { tool: 'search', outcome: 'answered' }, { actor: 'webmcp' });

    expect(telemetry.adapterName).toBe('recording');
    expect(actors).toEqual(['human', 'webmcp']);
  });

  it('keeps the no-op when the adapter fails to start', () => {
    telemetry.use(recording({ start: () => { throw new Error('no collector'); } }), context);

    expect(telemetry.adapterName).toBe('noop');
  });

  it('drops an adapter that throws, and the caller never sees it', () => {
    telemetry.use(recording({ event: () => { throw new Error('exporter exploded'); } }), context);

    expect(() => telemetry.trackEvent('webmcp_tool_invoked', { tool: 'search', outcome: 'answered' })).not.toThrow();
    expect(telemetry.adapterName).toBe('noop');
  });

  it('runs the work exactly once when the adapter fails before running it', () => {
    telemetry.use(recording({ span: () => { throw new Error('tracer gone'); } }), context);
    const work = vi.fn(() => 'paid');

    expect(telemetry.withSpan('checkout', work)).toBe('paid');
    expect(work).toHaveBeenCalledTimes(1);
  });

  it('lets the application\'s own error through, untouched and not retried', () => {
    telemetry.use(recording(), context);
    const failure = new Error('card declined');
    const work = vi.fn(() => { throw failure; });

    expect(() => telemetry.withSpan('checkout', work)).toThrow(failure);
    expect(work).toHaveBeenCalledTimes(1);
    expect(telemetry.adapterName).toBe('recording');
  });
});
