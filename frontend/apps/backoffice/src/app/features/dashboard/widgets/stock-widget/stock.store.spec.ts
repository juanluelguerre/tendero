import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { StockHealth } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { StockStore } from './stock.store';

const HEALTH: StockHealth = {
  period: 'Week',
  toRestock: 4,
  soldOut: 1,
  underTwoWeeks: 2,
  noSales: 7,
  warehouses: [{ warehouseCode: 'MAD', medianDays: 36 }],
  closest: [{ sku: 'BACKPACK', available: 18, perDay: 1.5, days: 12 }],
  fastest: [],
  slowest: [],
};

/** The stock at the shop's pace (R-9, D-7): what runs out soon, what does not sell, and the closest to running out. */
describe('the stock store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, DashboardSources, DashboardRefresh, StockStore],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('reads the counts and the closest to running out from the stock health', async () => {
    const store = TestBed.inject(StockStore);
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/stock?period=Week').flush(HEALTH);
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.restock(), store.soldOut(), store.soon(), store.noSales(), store.closest().map((sku) => sku.sku)]).toEqual([4, 1, 2, 7, ['BACKPACK']]);
  });
});
