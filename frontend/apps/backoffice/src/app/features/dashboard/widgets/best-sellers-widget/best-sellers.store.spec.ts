import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { SalesFigures, SalesSummary } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { BestSellersStore } from './best-sellers.store';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const figures: SalesFigures = { revenue: eur(0), orders: 0, units: 0, averageOrderValue: eur(0), unitsPerOrder: 0 };

const summary = (period: DashboardPeriod, skus: string[]): SalesSummary => ({
  period,
  current: figures,
  previous: figures,
  bestSellers: skus.map((sku, index) => ({ sku, name: sku, units: 10 - index, revenue: eur(100 - index) })),
});

/** Best sellers (R-9, D-7): the period's sales summary, in the order Ordering ranked it, for the period the dashboard is on. */
describe('the best sellers store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        BestSellersStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('keeps the best sellers in the order Ordering ranked them', async () => {
    const store = TestBed.inject(BestSellersStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/sales?period=Week').flush(summary('Week', ['MUG', 'PAN', 'LAMP']));
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.sales.value()?.bestSellers.map((seller) => seller.sku)).toEqual(['MUG', 'PAN', 'LAMP']);
  });

  it('asks again for the period the dashboard moves to, and reads that one', async () => {
    const period = signal<DashboardPeriod>('Week');
    const store = TestBed.inject(BestSellersStore);
    TestBed.runInInjectionContext(() => store.followPeriod(period));
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/sales?period=Week').flush(summary('Week', ['MUG']));
    await TestBed.inject(ApplicationRef).whenStable();

    period.set('Month');
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/sales?period=Month').flush(summary('Month', ['RUG']));
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.sales.value()?.period, store.sales.value()?.bestSellers[0]?.sku]).toEqual(['Month', 'RUG']);
  });
});
