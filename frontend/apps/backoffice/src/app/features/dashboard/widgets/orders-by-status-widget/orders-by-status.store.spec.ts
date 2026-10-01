import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { OperationsSummary } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { OrdersByStatusStore } from './orders-by-status.store';

const WORDS = {
  dashboard: {
    ordersByStatus: {
      title: 'Orders by status',
      toShip: 'To ship',
      inProgress: 'In progress',
      paymentFailed: 'Payment failed',
      cancelledCount: 'Cancelled',
    },
  },
};

/** Orders by status (R-9, D-7): the operations' four counts as one bar each, labelled in the reader's language. */
describe('the orders by status store', () => {
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
        OrdersByStatusStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  function asked() {
    const store = TestBed.inject(OrdersByStatusStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(WORDS);
    return store;
  }

  it('draws to ship, in progress, payment failed and cancelled, each beside its own count', async () => {
    const store = asked();
    http
      .expectOne('http://api/api/dashboard/operations?period=Week')
      .flush({ toShip: 7, inProgress: 3, paymentFailed: 1, cancelled: 2 } satisfies Partial<OperationsSummary>);
    await TestBed.inject(ApplicationRef).whenStable();
    const chart = store.chart();

    expect(chart?.categories.map((category, index) => [category, chart.series[0]?.values[index]])).toEqual([
      ['To ship', 7],
      ['In progress', 3],
      ['Payment failed', 1],
      ['Cancelled', 2],
    ]);
  });

  it('has no chart until the operations answer', () => {
    const store = asked();

    expect(store.chart()).toBeNull();
  });
});
