import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { KeyFiguresStore } from './key-figures.store';

const Api = 'http://api/api/dashboard';

/** The store without its widget (ADR 0047): what the server answered, and what the store makes of it. */
describe('the key figures store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        KeyFiguresStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  function open(period: DashboardPeriod) {
    const store = TestBed.inject(KeyFiguresStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal(period)));
    TestBed.tick();
    return store;
  }

  it('asks nothing until it has a period, then each source once for it', () => {
    const store = TestBed.inject(KeyFiguresStore);
    TestBed.tick();
    expect(http.match(() => true)).toHaveLength(0);

    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Day')));
    TestBed.tick();

    expect(
      http
        .match((req) => req.url.startsWith(Api))
        .map((req) => `${req.request.url.slice(Api.length)}?${req.request.params}`)
        .sort(),
    ).toEqual(['/funnel?period=Day', '/operations?period=Day', '/sales/series?period=Day', '/sales?period=Day']);
  });

  /** Review focus 2, at the widget's own level: one source failing takes away its figure and nothing else. */
  it('keeps what answered when one source fails', async () => {
    const store = open('Week');
    http
      .expectOne(`${Api}/sales?period=Week`)
      .flush({ period: 'Week', currency: 'EUR', current: {}, previous: {}, bestSellers: [] });
    http.expectOne(`${Api}/sales/series?period=Week`).flush({ current: [], previous: [] });
    http.expectOne(`${Api}/funnel?period=Week`).flush('down', { status: 503, statusText: 'Unavailable' });
    http.expectOne(`${Api}/operations?period=Week`).flush({ returnRate: 0.05 });
    // A resource takes its answer a microtask after the response.
    await TestBed.inject(ApplicationRef).whenStable();
    const sources = store.sources();

    expect([!!sources.sales, !!sources.series, sources.funnel, sources.operations?.returnRate]).toEqual([
      true,
      true,
      undefined,
      0.05,
    ]);
  });
});
