import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { OperationsSummary } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { HealthStore } from './health.store';

const operations = (overrides: Partial<OperationsSummary>): Partial<OperationsSummary> => ({
  cancelled: 1,
  cancellationRate: 0.02,
  cancellationTarget: 0.025,
  cancellationStanding: 'near',
  defects: 1,
  defectRate: 0.005,
  defectTarget: 0.01,
  defectStanding: 'within',
  lateShipments: 2,
  shippedCounted: 20,
  shippedBeforeMoments: 3,
  lateShipmentRate: 0.1,
  lateShipmentTarget: 0.04,
  lateShipmentStanding: 'outside',
  ...overrides,
});

/** Health's three rates against their targets (R-9, D-7), each saying no data rather than 0 % when there is none. */
describe('the health store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, DashboardSources, DashboardRefresh, HealthStore],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function answered(body: Partial<OperationsSummary>) {
    const store = TestBed.inject(HealthStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/operations?period=Week').flush(body);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('reads the cancellation, defect and late shipment rates, each against its target', async () => {
    const store = await answered(operations({}));

    expect(store.rates().map((rate) => [rate.key, rate.rate, rate.standing])).toEqual([
      ['cancellation', 0.02, 'near'],
      ['defect', 0.005, 'within'],
      ['lateShipment', 0.1, 'outside'],
    ]);
  });

  it('says a rate with nothing to divide is no data, and keeps the orders shipped before the moments apart', async () => {
    const store = await answered(operations({ lateShipmentRate: null, lateShipmentStanding: null, shippedCounted: 0 }));
    const late = store.rates().find((rate) => rate.key === 'lateShipment');

    expect([late?.rate, late?.standing, late?.apart]).toEqual([null, null, 3]);
  });
});
