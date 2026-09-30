import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { StockWidget } from './stock-widget';
import { openWidget, setUpWidgets } from '../widget-testing';

describe('the stock widget', () => {
  beforeEach(setUpWidgets);
  afterEach(() => TestBed.inject(HttpTestingController).match(() => true));

  /** R-9, D-7: what to restock and what is sold out, and at the shop's pace what runs out soon and what does not sell. */
  it('counts what to restock, what is sold out, what runs out soon and what does not sell, and lists the closest', async () => {
    const element = await openWidget(
      StockWidget,
      {
        '/api/dashboard/stock': {
          period: 'Week',
          toRestock: 5,
          soldOut: 2,
          underTwoWeeks: 3,
          noSales: 7,
          warehouses: [
            { warehouseCode: 'MAD', medianDays: 36 },
            { warehouseCode: 'BCN', medianDays: null },
          ],
          closest: [
            { sku: 'A', available: 3, perDay: 1, days: 3 },
            { sku: 'B', available: 18, perDay: 1.5, days: 12 },
          ],
          fastest: [],
          slowest: [],
        },
      },
      {},
    );

    expect([...element.querySelectorAll('.fact__value')].map((value) => value.textContent?.trim())).toEqual(['5', '2', '3', '7']);
    expect([...element.querySelectorAll('.ranking__name')].map((name) => name.textContent?.trim())).toEqual(['A', 'B']);
    expect(element.textContent).toContain('12 days · 18 left');
    // The spec's median per warehouse (R-9, D-7), and a warehouse whose stock does not sell said so.
    expect(element.textContent).toContain('MAD: 36 days');
    expect(element.textContent).toContain('BCN: no sales');
  });
});
