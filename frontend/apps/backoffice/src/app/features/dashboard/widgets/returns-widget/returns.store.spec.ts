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
import { ReturnsStore } from './returns.store';

const WORDS = {
  returns: { reason: { Damaged: 'Arrived damaged', WrongSize: 'Wrong size' } },
  dashboard: { returns: { openReturns: 'Open returns' } },
};

/** Returns (R-9, D-7): the open returns ranked by reason, in the returns screen's own words, and no chart when none is open. */
describe('the returns store', () => {
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
        ReturnsStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  async function answered(openReturnsByReason: OperationsSummary['openReturnsByReason']) {
    const store = TestBed.inject(ReturnsStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(WORDS);
    http
      .expectOne('http://api/api/dashboard/operations?period=Week')
      .flush({ openReturnsByReason } satisfies Partial<OperationsSummary>);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('ranks the open returns by reason, named as the returns screen names them', async () => {
    const store = await answered([
      { reason: 'Damaged', count: 4 },
      { reason: 'WrongSize', count: 2 },
    ]);
    const chart = store.chart();

    expect([chart?.categories, chart?.series[0]?.label, chart?.series[0]?.values]).toEqual([
      ['Arrived damaged', 'Wrong size'],
      'Open returns',
      [4, 2],
    ]);
  });

  it('has no chart when no return is open', async () => {
    const store = await answered([]);

    expect(store.chart()).toBeNull();
  });
});
