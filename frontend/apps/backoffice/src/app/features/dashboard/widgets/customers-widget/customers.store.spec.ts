import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SalesSeries } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { CustomersStore } from './customers.store';

const point = (start: string, newCustomers: number, returningCustomers: number) => ({
  start,
  revenue: 0,
  orders: 0,
  units: 0,
  agentOrders: 0,
  agentRevenue: 0,
  newCustomers,
  returningCustomers,
});

const SERIES: SalesSeries = {
  period: 'Week',
  currency: 'EUR',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [point('2026-10-12T00:00:00+02:00', 3, 1), point('2026-10-13T00:00:00+02:00', 0, 2)],
  previous: [point('2026-10-05T00:00:00+02:00', 1, 0), point('2026-10-06T00:00:00+02:00', 0, 0)],
};

/** Customers day by day (R-9, D-7): who bought for the first time and who came back, from the server's series. */
describe('the customers store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }, DashboardSources, DashboardRefresh, CustomersStore],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  it('draws new and returning buyers for each day of the period', async () => {
    const store = TestBed.inject(CustomersStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush({});
    http.expectOne('http://api/api/dashboard/funnel?period=Week').flush({ newCustomers: 3, returningCustomers: 3 });
    http.expectOne('http://api/api/dashboard/sales/series?period=Week').flush(SERIES);
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.byDay()?.categories.length, store.byDay()?.series.map((series) => series.values)]).toEqual([2, [[3, 0], [1, 2]]]);
  });
});
