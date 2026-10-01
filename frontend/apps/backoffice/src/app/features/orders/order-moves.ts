import type { OrderMove } from '../../data-access/ordering.service';
import type { BackofficeAction } from '../../usage';

/**
 * What the screen offers for a status. **Not the state machine**: which moves are
 * legal is `Order.AllowedTransitions` on the server, and when one of these is
 * wrong the server answers 409 with a sentence the screen shows as it came.
 */

/** Each move as the usage event names it (ADR 0041). */
export const OrderActions = {
  Ship: 'order_ship',
  Deliver: 'order_deliver',
  Cancel: 'order_cancel',
} as const satisfies Record<OrderMove, BackofficeAction>;

/** The likely move for a status: what the row and the pane lead with. Cancelling is never it. */
export function primaryMove(status: string): OrderMove | null {
  switch (status) {
    case 'Confirmed':
      return 'Ship';
    case 'Shipped':
      return 'Deliver';
    default:
      return null;
  }
}

/** Whether the server's table would let this status be cancelled; it still has the last word. */
export function cancellable(status: string): boolean {
  return ['Pending', 'PaymentAuthorized', 'PaymentFailed', 'Confirmed'].includes(status);
}
