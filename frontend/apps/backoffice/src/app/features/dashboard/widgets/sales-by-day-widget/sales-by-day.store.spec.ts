import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SalesPoint, SalesSeries } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { SalesByDayStore } from './sales-by-day.store';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const WORDS = {
  dashboard: {
    salesByDay: {
      current: { Week: 'This week', Month: 'This month' },
      previous: { Week: 'Last week', Month: 'Last month' },
    },
  },
};

const point = (start: string, revenue: number): SalesPoint => ({
  start,
  revenue: eur(revenue),
  orders: 1,
  units: 1,
  agentOrders: 0,
  agentRevenue: eur(0),
  newCustomers: 0,
  returningCustomers: 0,
});

const WEEK: SalesSeries = {
  period: 'Week',
  timeZone: 'Europe/Madrid',
  step: 'Day',
  current: [point('2026-10-12T00:00:00+02:00', 120), point('2026-10-13T00:00:00+02:00', 80)],
  previous: [point('2026-10-05T00:00:00+02:00', 90), point('2026-10-06T00:00:00+02:00', 0)],
};

/** Sales by day (R-9, D-7): the current period against the previous one, each named in the words of the window it draws. */
describe('the sales by day store', () => {
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
        SalesByDayStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  function asked() {
    const store = TestBed.inject(SalesByDayStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(WORDS);
    return store;
  }

  it('draws this week against last week, each in its own words, on the shop’s days', async () => {
    const store = asked();
    http.expectOne('http://api/api/dashboard/sales/series?period=Week').flush(WEEK);
    await TestBed.inject(ApplicationRef).whenStable();
    const chart = store.chart();

    expect([chart?.categories, chart?.series.map((series) => [series.label, series.role, series.values])]).toEqual([
      ['Mon', 'Tue'],
      [
        ['This week', 'data', [120, 80]],
        ['Last week', 'previous', [90, 0]],
      ],
    ]);
  });

  it('has no chart until the series answers', () => {
    const store = asked();

    expect(store.chart()).toBeNull();
  });
});
