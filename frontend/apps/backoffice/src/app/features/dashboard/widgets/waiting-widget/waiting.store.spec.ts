import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ClaimForReview, ClaimsForReviewPage, StockList, StockRow } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { WaitingStore } from './waiting.store';

const row = (sku: string, warehouseCode: string, available: number): Partial<StockRow> => ({
  sku,
  warehouseCode,
  available,
});

const STOCK: Partial<StockList> = {
  rows: [row('MUG', 'MAD', 3), row('MUG', 'BCN', 4), row('LAMP', 'MAD', 2), row('BAG', 'BCN', 2)] as StockRow[],
  counts: [
    { filter: 'All', count: 100 },
    { filter: 'Restock', count: 3 },
    { filter: 'SoldOut', count: 1 },
  ],
};

const CLAIMS: Partial<ClaimsForReviewPage> = {
  items: [{ claimId: 'weakest' }, { claimId: 'weak' }] as ClaimForReview[],
};

/** What is waiting for the shopkeeper (R-9, D-7): the weakest claims in the queue's order, and what to restock, per SKU, lowest first. */
describe('the waiting store', () => {
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
        WaitingStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  function asked() {
    const store = TestBed.inject(WaitingStore);
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush({});
    http.expectOne((req) => req.url === 'http://api/api/catalog/products').flush({ items: [], total: 2 });
    return store;
  }

  it('sums each SKU over the warehouses, lowest first, and counts what the server says to restock', async () => {
    const store = asked();
    http.expectOne((req) => req.url === 'http://api/api/inventory/stock').flush(STOCK);
    http.expectOne((req) => req.url === 'http://api/api/knowledge/claims').flush(CLAIMS);
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.lowStock(), store.restockCount()]).toEqual([
      [
        { sku: 'BAG', available: 2 },
        { sku: 'LAMP', available: 2 },
        { sku: 'MUG', available: 7 },
      ],
      3,
    ]);
  });

  it('names the claims in the order the queue gave them', async () => {
    const store = asked();
    http.expectOne((req) => req.url === 'http://api/api/inventory/stock').flush(STOCK);
    http.expectOne((req) => req.url === 'http://api/api/knowledge/claims').flush(CLAIMS);
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.weakestClaims().map((claim) => claim.claimId)).toEqual(['weakest', 'weak']);
  });

  it('has nothing to restock and no claims to name until they answer', () => {
    const store = asked();

    expect([store.lowStock(), store.restockCount(), store.weakestClaims()]).toEqual([[], 0, []]);
  });
});
