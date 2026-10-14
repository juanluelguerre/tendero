import type { CheckoutFunnel, OperationsSummary, SalesSeries, SalesSummary } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { keyFigures } from './key-figures';

/** Money as the API sends it: minor units and its currency. */
const eur = (amount: number) => ({ amount: Math.round(amount * 100), currency: 'EUR' });

const say = {
  money: (amount: { amount: number }) => `€${amount.amount / 100}`,
  share: (ratio: number) => `${Math.round(ratio * 100)}%`,
  decimal: (value: number) => value.toFixed(2),
};
const figures = (revenue: number, orders: number, units: number, averageOrderValue: number) => ({
  revenue: eur(revenue),
  orders,
  units,
  averageOrderValue: eur(averageOrderValue),
  unitsPerOrder: Math.round((units / orders) * 100) / 100,
});
const SALES = {
  period: 'Week',
  current: figures(900, 9, 12, 100),
  previous: figures(600, 6, 9, 100),
  bestSellers: [],
} as unknown as SalesSummary;
const point = (revenue: number, orders: number, units: number) => ({
  start: '2026-10-12T00:00:00+02:00',
  revenue: eur(revenue),
  orders,
  units,
});
const SERIES = {
  current: [point(300, 3, 4), point(0, 0, 0), point(600, 6, 8)],
  previous: [],
} as unknown as SalesSeries;

describe('the key figures', () => {
  it('says revenue, orders, units and the average order with their change and their trend', () => {
    const result = keyFigures({ sales: SALES, series: SERIES }, say);

    expect(result.map((figure) => [figure.key, figure.value, figure.change?.direction, figure.trend])).toEqual([
      ['revenue', '€900', 'up', [300, 0, 600]],
      ['orders', '9', 'up', [3, 0, 6]],
      ['units', '12', 'up', [4, 0, 8]],
      // A day with no orders has no average: 0 on the line, never NaN.
      ['averageOrder', '€100', 'flat', [100, 0, 100]],
      // How full a basket is (R-9, D-7): 12 units in 9 orders, against 9 in 6.
      ['unitsPerOrder', '1.33', 'down', [4 / 3, 0, 8 / 6]],
    ]);
  });

  it('draws no trend until the series has arrived', () => {
    expect(keyFigures({ sales: SALES }, say).every((figure) => figure.trend === null)).toBe(true);
  });

  /** Plan 2's ruling: no previous period for these two today, so no change and no trend rather than an invented one. */
  it('says conversion and the return rate bare, once their sources arrive', () => {
    const funnel = { conversion: 0.25 } as CheckoutFunnel;
    const operations = { returnRate: 0.05 } as OperationsSummary;

    expect(keyFigures({ funnel, operations }, say)).toEqual([
      { key: 'conversion', value: '25%', change: null, trend: null },
      { key: 'returnRate', value: '5%', change: null, trend: null },
    ]);
  });
});
