import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { AgentSalesStore } from './agent-sales.store';

const point = (start: string, agentRevenue: number) => ({
  start,
  revenue: agentRevenue * 2,
  orders: 2,
  units: 2,
  agentOrders: agentRevenue > 0 ? 1 : 0,
  agentRevenue,
  newCustomers: 0,
  returningCustomers: 0,
});

/** What agents buy, day by day (R-9, D-7): agents in clay, from the server's series. */
describe('the agent sales store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        AgentSalesStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  it('draws what agents bought on each day, as an agent series', async () => {
    const store = TestBed.inject(AgentSalesStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush({});
    http
      .expectOne('http://api/api/dashboard/agent-sales?period=Week')
      .flush({ orders: 4, agentOrders: 1, byAgent: [] });
    http.expectOne('http://api/api/dashboard/sales/series?period=Week').flush({
      period: 'Week',
      currency: 'EUR',
      timeZone: 'Europe/Madrid',
      step: 'Day',
      current: [point('2026-10-12T00:00:00+02:00', 40), point('2026-10-13T00:00:00+02:00', 0)],
      previous: [point('2026-10-05T00:00:00+02:00', 0), point('2026-10-06T00:00:00+02:00', 0)],
    });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.byDay()?.series.map((series) => [series.role, series.values])).toEqual([['agent', [40, 0]]]);
  });
});
