import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { createEnvironmentInjector, EnvironmentInjector, ErrorHandler, type Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ActivatedRouteSnapshot, provideRouter, Router } from '@angular/router';
import { API_BASE_URL } from '@tendero/shared-util';
import { routeTemplate, TELEMETRY_ADAPTERS, TelemetryErrorHandler, TelemetryStartup } from './provide-telemetry';
import { sessionId } from './session';
import { Telemetry, type TelemetryAdapter, type TelemetryContext } from './telemetry';

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

  it("starts the adapter with the app's name, the session and the page's own endpoint", async () => {
    const contexts: TelemetryContext[] = [];
    loaders['otel'] = () => Promise.resolve(adapter('otel', { start: (context) => void contexts.push(context) }));

    await start((request) => request.flush({ adapter: 'otel' }));

    expect(contexts).toEqual([{ service: 'storefront', sessionId: sessionId(), endpoint: '/otlp' }]);
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
    loaders['otel'] = () =>
      Promise.resolve(
        adapter('otel', {
          start: () => {
            throw new Error('no provider');
          },
        }),
      );
    expect(await start((request) => request.flush({ adapter: 'otel' }))).toBe('noop');
  });
});

describe('routeTemplate: the name every page view carries', () => {
  /** A chain of active routes, each with the path its configuration declares. */
  function chain(...paths: (string | undefined)[]): ActivatedRouteSnapshot {
    return paths.reduceRight<ActivatedRouteSnapshot | null>(
      (child, path) =>
        ({ routeConfig: path === undefined ? null : { path }, firstChild: child }) as unknown as ActivatedRouteSnapshot,
      null,
    ) as ActivatedRouteSnapshot;
  }

  it('joins each level of the route with a slash, from the root', () => {
    expect(routeTemplate(chain(undefined, ':lang', 'p/:code'))).toBe('/:lang/p/:code');
  });

  it('skips a level that adds no path: the root, a layout, an empty child', () => {
    expect(routeTemplate(chain(undefined, '', ':lang', '', 'cart'))).toBe('/:lang/cart');
  });

  it('is the root when nothing below it has a path', () => {
    expect(routeTemplate(chain(undefined))).toBe('/');
    expect(routeTemplate(chain(undefined, ''))).toBe('/');
  });
});

describe('TelemetryStartup: following the route', () => {
  let routes: string[];

  beforeEach(() => {
    routes = [];
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([{ path: ':lang', children: [{ path: 'p/:code', children: [] }] }]),
        { provide: API_BASE_URL, useValue: '' },
        {
          provide: TELEMETRY_ADAPTERS,
          useValue: {
            otel: () => Promise.resolve(adapter('otel', { route: (template) => void routes.push(template) })),
          },
        },
      ],
    });
  });

  async function started(injector: Injector = TestBed.inject(EnvironmentInjector)): Promise<void> {
    const starting = injector.get(TelemetryStartup).start({ service: 'storefront' });
    TestBed.inject(HttpTestingController).expectOne('/api/telemetry/config').flush({ adapter: 'otel' });
    await starting;
  }

  it('reports the page it starts on, then the template of every page it reaches — never the URL', async () => {
    await started();
    await TestBed.inject(Router).navigateByUrl('/es/p/ABC-123');

    expect(routes).toEqual(['/', '/:lang/p/:code']);
  });

  it('stops following the route when the app is destroyed', async () => {
    // The app's own injector, so destroying it is the app going away and not the test's.
    const app = createEnvironmentInjector([TelemetryStartup], TestBed.inject(EnvironmentInjector));
    await started(app);
    app.destroy();

    await TestBed.inject(Router).navigateByUrl('/es/p/ABC-123');

    expect(routes).toEqual(['/']);
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
