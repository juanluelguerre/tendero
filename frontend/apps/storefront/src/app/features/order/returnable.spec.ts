import { describe, expect, it } from 'vitest';
import type { OrderLineView, OrderView, ReturnableLineView } from '@tendero/shared-api';
import { returnableLines } from './returnable';

/**
 * What the order page offers to send back.
 *
 * The UI review of 2026-09-11 returned one shirt twice through this page: the
 * shirt was still offered after its return was open, and the server accepted
 * the second request too. The server is fixed; this is the other half — a page
 * that offers exactly what the domain says is left, rather than every line the
 * order ever had.
 */
function anOrder(...lines: Array<[sku: string, quantity: number]>): OrderView {
  return {
    orderId: 'o1',
    lines: lines.map(
      ([sku, quantity]) =>
        ({ sku, productName: sku, variantLabel: null, unitPrice: 20, quantity }) as OrderLineView,
    ),
  } as unknown as OrderView;
}

const left = (...pairs: Array<[sku: string, quantity: number]>): ReturnableLineView[] =>
  pairs.map(([sku, quantity]) => ({ sku, quantity }));

describe('what can still be returned', () => {
  it('offers a line whose return is not open yet, up to what was bought', () => {
    const lines = returnableLines(anOrder(['SHIRT', 3]), left(['SHIRT', 3]));

    expect(lines.map((line) => [line.sku, line.left])).toEqual([['SHIRT', 3]]);
  });

  it('stops offering a line that is already on its way back', () => {
    const lines = returnableLines(anOrder(['SHIRT', 1], ['PANS', 1]), left(['SHIRT', 0], ['PANS', 1]));

    expect(lines.map((line) => line.sku)).toEqual(['PANS']);
  });

  it('offers only what is left of a line that is partly coming back', () => {
    const lines = returnableLines(anOrder(['SHIRT', 3]), left(['SHIRT', 1]));

    expect(lines.map((line) => [line.sku, line.left])).toEqual([['SHIRT', 1]]);
  });

  /**
   * A SKU the server said nothing about is not returnable. Defaulting to the
   * ordered quantity would put the old behaviour back the day the field goes
   * missing — an offer the command then refuses.
   */
  it('offers nothing for a line the server gave no answer about', () => {
    expect(returnableLines(anOrder(['SHIRT', 2]), [])).toEqual([]);
  });
});
