import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { SellThroughStore } from './sell-through.store';

/** Sell-through for the period (R-9, D-7): what moves and what sits, as the server ranked them. */
describe('the sell-through store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, DashboardSources, DashboardRefresh, SellThroughStore],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('reads what moves and what sits for the period it is given', async () => {
    const store = TestBed.inject(SellThroughStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Month')));
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/stock?period=Month').flush({
      period: 'Month', toRestock: 0, soldOut: 0, underTwoWeeks: 0, noSales: 0, warehouses: [], closest: [],
      fastest: [{ sku: 'PAN', sold: 3, left: 0, rate: 1 }],
      slowest: [{ sku: 'MUG', sold: 0, left: 5, rate: 0 }],
    });
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.moves().map((row) => row.sku), store.sits().map((row) => row.sku)]).toEqual([['PAN'], ['MUG']]);
  });
});
