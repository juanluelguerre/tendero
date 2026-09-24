import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { TELEMETRY_ADAPTERS, TelemetryErrorHandler, TelemetryStartup } from './provide-telemetry';
import { Telemetry, type TelemetryAdapter } from './telemetry';

function adapter(name: string, overrides: Partial<TelemetryAdapter> = {}): TelemetryAdapter {
  return {
    name,
    start: () => undefined,
    event: () => undefined,
    error: () => undefined,
    span: (_name, _attributes, work) => work(),
    route: () => undefined,
    ...overrides,
  };
}

describe('TelemetryStartup: every way it can fail ends in the no-op', () => {
  let http: HttpTestingController;
  let loaders: Record<string, () => Promise<TelemetryAdapter>>;

  beforeEach(() => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    loaders = { otel: () => Promise.resolve(adapter('otel')) };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
        { provide: TELEMETRY_ADAPTERS, useFactory: () => loaders },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function start(answer: (request: ReturnType<HttpTestingController['expectOne']>) => void): Promise<string> {
    const started = TestBed.inject(TelemetryStartup).start({ service: 'storefront' });
    answer(http.expectOne('/api/telemetry/config'));
    await started;
    return TestBed.inject(Telemetry).adapterName;
  }

  it('loads the adapter the API names', async () => {
    expect(await start((request) => request.flush({ adapter: 'otel' }))).toBe('otel');
  });

  it('stays on the no-op when the API does not answer', async () => {
    expect(await start((request) => request.error(new ProgressEvent('offline')))).toBe('noop');
  });

  it('stays on the no-op when the API names no adapter this page knows', async () => {
    expect(await start((request) => request.flush({ adapter: 'noop' }))).toBe('noop');
  });

  it('stays on the no-op when the SDK fails to download', async () => {
    loaders['otel'] = () => Promise.reject(new Error('chunk load failed'));
    expect(await start((request) => request.flush({ adapter: 'otel' }))).toBe('noop');
  });

  it('stays on the no-op when the SDK fails to start', async () => {
    loaders['otel'] = () => Promise.resolve(adapter('otel', { start: () => { throw new Error('no provider'); } }));
    expect(await start((request) => request.flush({ adapter: 'otel' }))).toBe('noop');
  });
});

describe('TelemetryErrorHandler', () => {
  it('records the error and still lets Angular report it', () => {
    const errors: unknown[] = [];
    vi.spyOn(console, 'error').mockImplementation(() => undefined);
    TestBed.configureTestingModule({ providers: [{ provide: ErrorHandler, useClass: TelemetryErrorHandler }] });
    TestBed.inject(Telemetry).use(adapter('recording', { error: (error) => void errors.push(error) }), {
      service: 'storefront',
      sessionId: 'abc',
      endpoint: '/otlp',
    });

    const failure = new Error('render failed');
    TestBed.inject(ErrorHandler).handleError(failure);

    expect(errors).toEqual([failure]);
    expect(console.error).toHaveBeenCalled();
  });
});
