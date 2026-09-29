import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { StockRow } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StockStore } from './stock.store';

const Stock = 'http://api/api/inventory/stock';

const row = (sku: string, onHand: number) => ({ sku, warehouseCode: 'MAD', onHand, available: onHand }) as unknown as StockRow;

/** The shelves' state (ADR 0047): the filter, search and page in the URL, the rows, and the counts being typed. */
describe('the stock store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }, StockStore],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(StockStore);
    TestBed.tick();
    http
      .match((req) => req.url === Stock && req.method === 'GET')
      .forEach((req) => req.flush({ rows: [row('A', 3)], total: 1, counts: [{ filter: 'SoldOut', count: 2 }], reservations: [], restockAt: 5 }));
    await new Promise((resolve) => setTimeout(resolve));
    return store;
  }

  /** `restock` is a link a person could type: read in any case, as the server reads it. */
  it('reads the filter in any case, and opens on everything without one', async () => {
    const restock = await at('/?filter=restock');
    const first = restock.filter();
    await TestBed.inject(Router).navigateByUrl('/');

    expect([first, restock.filter()]).toEqual(['Restock', 'All']);
  });

  it("saves a count and shows the server's numbers, not the typed one", async () => {
    const store = await at('/');
    store.draft(row('A', 3), 7);
    store.save(row('A', 3));
    http.expectOne(`${Stock}/A/MAD`).flush({ onHand: 7, available: 5 });

    expect([store.rows()[0].available, store.changed(store.rows()[0]), store.saved()]).toEqual([5, false, 'A@MAD']);
  });

  /** An unchanged or negative count is not a count: nothing is sent. */
  it('does not send a count that says nothing new', async () => {
    const store = await at('/');
    store.draft(row('A', 3), 3);
    store.save(row('A', 3));
    store.draft(row('A', 3), -1);
    store.save(row('A', 3));

    expect(http.match((req) => req.method === 'PUT')).toHaveLength(0);
  });
});
