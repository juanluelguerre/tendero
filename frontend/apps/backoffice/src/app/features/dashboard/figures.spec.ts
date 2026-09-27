import { describe, expect, it } from 'vitest';
import { changeOf, formatChange, formatShare, lowStockOf, shareOf } from './figures';

/**
 * How the dashboard says a figure moved. The one rule worth a test: against a
 * period that had nothing, there is a direction and no percentage — "+∞ %" helps
 * nobody, and "+100 %" would be a lie.
 */
describe('a figure against the previous period', () => {
  it('says the direction and the relative change', () => {
    expect(changeOf(125, 100)).toEqual({ direction: 'up', ratio: 0.25 });
    expect(changeOf(90, 100)).toEqual({ direction: 'down', ratio: -0.1 });
    expect(changeOf(100, 100)).toEqual({ direction: 'flat', ratio: 0 });
  });

  it('has a direction and no percentage when the previous period had nothing', () => {
    expect(changeOf(40, 0)).toEqual({ direction: 'up', ratio: null });
    expect(changeOf(0, 0)).toEqual({ direction: 'flat', ratio: null });
    expect(formatChange(changeOf(40, 0), 'en')).toBeNull();
  });

  it('is written the reader’s way', () => {
    expect(formatChange(changeOf(125, 100), 'en')).toBe('+25%');
    expect(formatShare(0.1667, 'en')).toBe('16.7%');
    expect(formatShare(0.1667, 'es')).toBe('16,7 %');
  });

  it('takes a share of nothing as nothing', () => {
    expect(shareOf(3, 0)).toBe(0);
    expect(shareOf(1, 4)).toBe(0.25);
  });
});

describe('stock running low', () => {
  it('is counted per SKU across warehouses, not per warehouse row', () => {
    const rows = [
      { sku: 'A', available: 0 },
      { sku: 'A', available: 40 },
      { sku: 'B', available: 2 },
      { sku: 'B', available: 1 },
      { sku: 'C', available: 5 },
    ];

    expect(lowStockOf(rows)).toEqual([
      { sku: 'B', available: 3 },
      { sku: 'C', available: 5 },
    ]);
  });
});
