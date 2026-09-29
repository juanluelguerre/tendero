import type { ReturnView } from '@tendero/shared-api';
import type { ReturnDecision } from '../../data-access/ordering.service';
import type { BackofficeAction } from '../../usage';

/**
 * What the screen offers for a return's status. **Not the state machine**: which
 * decisions are legal is the server's, and when one of these is wrong it answers
 * 409 with a sentence the screen shows as it came.
 */

/** Each decision as the usage event names it (ADR 0041). */
export const ReturnActions = {
  Approve: 'return_approve',
  Reject: 'return_reject',
  Receive: 'return_receive',
  Refund: 'return_refund',
} as const satisfies Record<ReturnDecision, BackofficeAction>;

/** The likely next decision: what the row and the pane lead with. Rejecting is never it. */
export function nextDecision(request: ReturnView): ReturnDecision | null {
  switch (request.status) {
    case 'Requested':
      return 'Approve';
    case 'Approved':
      return 'Receive';
    case 'Received':
      return 'Refund';
    default:
      return null;
  }
}

/**
 * Rejected FROM received is the edge that is easy to leave out: the parcel
 * arrived and the goods are not what the reason claimed.
 */
export function rejectable(request: ReturnView): boolean {
  return request.status === 'Requested' || request.status === 'Received';
}
