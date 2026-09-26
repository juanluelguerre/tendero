import { HttpClient } from '@angular/common/http';
import {
  DestroyRef,
  ErrorHandler,
  inject,
  Injectable,
  InjectionToken,
  makeEnvironmentProviders,
  provideAppInitializer,
  type EnvironmentProviders,
} from '@angular/core';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import type { TelemetryConfiguration } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { filter, firstValueFrom } from 'rxjs';
import { sessionId } from './session';
import { Telemetry, type TelemetryAdapter } from './telemetry';

export interface TelemetryOptions {
  /** The service name every record carries: `storefront` or `backoffice`. */
  readonly service: string;
}

/**
 * The adapters a page can load, by the name `/api/telemetry/config` answers
 * with. Each is a dynamic import, so a page downloads only the SDK it uses —
 * the reason the OpenTelemetry adapter is not in the main bundle at all.
 * Injectable so a test can hand in an adapter that fails to load.
 */
export const TELEMETRY_ADAPTERS = new InjectionToken<Readonly<Record<string, () => Promise<TelemetryAdapter>>>>(
  'TELEMETRY_ADAPTERS',
  {
    providedIn: 'root',
    factory: () => ({
      otel: () => import('./otel-adapter').then((module) => module.createOtelAdapter()),
      // The second adapter (P9b-8). It reaches a receiver only in the full
      // observability mode: the Aspire dashboard reads OTLP, not Faro's payload.
      faro: () => import('./faro-adapter').then((module) => module.createFaroAdapter()),
    }),
  },
);

/** Where every adapter posts: the page's own origin, which the dev server proxies (ADR 0036). */
export const TELEMETRY_ENDPOINT = '/otlp';

/**
 * Browser telemetry for one app. The page never waits for it: the app starts
 * with the no-op adapter, and the configured one takes over when it has loaded.
 * A configuration that cannot be read, an adapter that is not known, an import
 * that fails and a start that throws all end the same way — the no-op, and a
 * shop that behaves exactly as it would without any of this.
 */
export function provideTelemetry(options: TelemetryOptions): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: ErrorHandler, useClass: TelemetryErrorHandler },
    provideAppInitializer(() => {
      // Not awaited, on purpose: returning the promise would hold the first
      // render until an SDK had downloaded.
      void inject(TelemetryStartup).start(options);
    }),
  ]);
}

@Injectable({ providedIn: 'root' })
export class TelemetryStartup {
  private readonly telemetry = inject(Telemetry);
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly adapters = inject(TELEMETRY_ADAPTERS);
  private readonly router = inject(Router, { optional: true });
  private readonly destroyRef = inject(DestroyRef);

  async start(options: TelemetryOptions): Promise<void> {
    try {
      const configuration = await firstValueFrom(
        this.http.get<TelemetryConfiguration>(`${this.baseUrl}/api/telemetry/config`),
      );

      const load = this.adapters[configuration.adapter];
      if (!load) return;

      this.telemetry.use(await load(), {
        service: options.service,
        sessionId: sessionId(),
        endpoint: TELEMETRY_ENDPOINT,
      });

      this.followTheRoute();
    } catch {
      // The API is not answering, or the SDK did not load: no telemetry, and
      // nothing else changes. The facade already holds the no-op.
    }
  }

  /** The route TEMPLATE, never the URL: `/:lang/p/:code` is one series, the URL one per product. */
  private followTheRoute(): void {
    const router = this.router;
    if (!router) return;

    const report = () => this.telemetry.route(routeTemplate(router.routerState.snapshot.root));
    report();

    const subscription = router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(report);
    this.destroyRef.onDestroy(() => subscription.unsubscribe());
  }
}

/** `/:lang/p/:code` from the active route chain; `/` for the root. */
export function routeTemplate(root: ActivatedRouteSnapshot): string {
  const segments: string[] = [];
  for (let route: ActivatedRouteSnapshot | null = root; route; route = route.firstChild) {
    const path = route.routeConfig?.path;
    if (path) segments.push(path);
  }
  return '/' + segments.join('/');
}

/**
 * Every error Angular sees reaches telemetry, and then the console as before.
 * `provideBrowserGlobalErrorListeners()` already routes uncaught errors and
 * rejections here, so this one class covers all three.
 */
@Injectable()
export class TelemetryErrorHandler extends ErrorHandler {
  private readonly telemetry = inject(Telemetry);

  override handleError(error: unknown): void {
    this.telemetry.recordError(error);
    super.handleError(error);
  }
}
