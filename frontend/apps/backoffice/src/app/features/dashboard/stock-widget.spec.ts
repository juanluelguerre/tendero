import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StockWidget } from './stock-widget';
import { openWidget, setUpWidgets } from './widget-testing';

const row = (sku: string, available: number) => ({ sku, available });

describe('the stock widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  it('counts what to restock and what is sold out from the server counts, and lists the lowest first', async () => {
    const element = await openWidget(
      StockWidget,
      {
        '/api/inventory/stock': {
          rows: [row('B', 2), row('A', 1), row('B', 1)],
          counts: [
            { filter: 'Restock', count: 5 },
            { filter: 'SoldOut', count: 2 },
            { filter: 'All', count: 100 },
          ],
          total: 3,
          page: 1,
          pageSize: 20,
          reservations: [],
          restockAt: 5,
        },
      },
      {},
    );

    expect([...element.querySelectorAll('.fact__value')].map((value) => value.textContent?.trim())).toEqual(['5', '2']);
    expect([...element.querySelectorAll('.ranking__name')].map((name) => name.textContent?.trim())).toEqual(['A', 'B']);
  });
});
