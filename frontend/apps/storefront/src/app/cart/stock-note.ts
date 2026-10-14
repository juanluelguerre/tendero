/** What of a basket line the shelf is asked about: how many it holds, how many can be bought now. */
export interface Stocked {
  quantity: number;
  available: number;
}

/** A note about a line's stock: its words, and whether the line asks for more than there is. */
export interface StockNote {
  key: string;
  params?: Record<string, unknown>;
  short: boolean;
}

/**
 * What the basket says of a line when the shelf is short, or nothing. It is a
 * note and not a rule: the number was read when the basket was, nobody holds
 * it, and the till asks again — that one refuses.
 */
export function stockNote({ quantity, available }: Stocked): StockNote | null {
  if (available <= 0) return { key: 'cart.stock.none', short: true };
  if (quantity < available) return null;
  if (quantity === available) return { key: 'cart.stock.last', short: false };

  return available === 1
    ? { key: 'cart.stock.onlyOne', short: true }
    : { key: 'cart.stock.onlySome', params: { count: available }, short: true };
}

/** Whether pressing "+" asks for something the shelf has. */
export function canAddOneMore({ quantity, available }: Stocked): boolean {
  return quantity < available;
}
