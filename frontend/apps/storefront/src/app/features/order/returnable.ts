import type { OrderLineView, OrderView, ReturnableLineView } from '@tendero/shared-api';

/** A line of the order with how many of it may still be sent back. */
export type ReturnableLine = OrderLineView & { left: number };

/**
 * The lines the order page offers for return, each capped at what is left.
 *
 * It does not decide which returns count — that is `ReturnRequest.Returnable`
 * on the server, and a second copy here is how the page and the command would
 * start to disagree. It only joins the server's answer to the order's lines, and
 * a SKU the server said nothing about is not offered: the command would refuse
 * it, and an offer that is refused is worse than no offer.
 */
export function returnableLines(order: OrderView, returnable: ReturnableLineView[]): ReturnableLine[] {
  const left = new Map(returnable.map((line) => [line.sku, line.quantity]));

  return order.lines
    .map((line) => ({ ...line, left: left.get(line.sku) ?? 0 }))
    .filter((line) => line.left > 0);
}
