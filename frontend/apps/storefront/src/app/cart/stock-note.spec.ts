import { describe, expect, it } from 'vitest';
import { canAddOneMore, stockNote } from './stock-note';

/**
 * What the basket says of a line when the shelf is short (found in a browser,
 * 2026-10-13: two of something there was one of went all the way to *Thanks*).
 * A note, never a block: the stock moves, and the till is what refuses.
 */
describe('what a basket line says about stock', () => {
  it('says nothing while there is more than the line asks for', () => {
    expect(stockNote({ quantity: 1, available: 5 })).toBeNull();
    expect(canAddOneMore({ quantity: 1, available: 5 })).toBe(true);
  });

  it('says these are the last ones when the line asks for all there is', () => {
    expect(stockNote({ quantity: 2, available: 2 })).toEqual({ key: 'cart.stock.last', short: false });
    expect(canAddOneMore({ quantity: 2, available: 2 })).toBe(false);
  });

  it('says how many there are when the line asks for more, one or several', () => {
    expect(stockNote({ quantity: 2, available: 1 })).toEqual({ key: 'cart.stock.onlyOne', short: true });
    expect(stockNote({ quantity: 5, available: 3 })).toEqual({
      key: 'cart.stock.onlySome',
      params: { count: 3 },
      short: true,
    });
  });

  it('says there is none', () => {
    expect(stockNote({ quantity: 1, available: 0 })).toEqual({ key: 'cart.stock.none', short: true });
    expect(canAddOneMore({ quantity: 1, available: 0 })).toBe(false);
  });
});
