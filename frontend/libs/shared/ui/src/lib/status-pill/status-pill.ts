import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { OrderStatus, ReturnStatus } from '@tendero/shared-api';

/** What a status says at a glance. The word is always there; the tone only makes it findable. */
export type StatusTone = 'muted' | 'warn' | 'neutral' | 'good' | 'bad';

/**
 * An order's status as a tone, once for both apps. In transit is neutral, not
 * clay: the accent is what an agent did (design/DESIGN.md), and an order on its
 * way is neither done nor waiting (the author, 2026-09-30).
 */
export function orderStatusTone(status: OrderStatus): StatusTone {
  switch (status) {
    case 'Pending':
      return 'muted';
    case 'PaymentAuthorized':
    case 'Confirmed':
      return 'warn';
    case 'Shipped':
      return 'neutral';
    case 'Delivered':
      return 'good';
    case 'PaymentFailed':
    case 'Cancelled':
      return 'bad';
  }
}

/** A return's status as a tone: received is on its way to a refund, and neutral like a shipment. */
export function returnStatusTone(status: ReturnStatus): StatusTone {
  switch (status) {
    case 'Requested':
      return 'muted';
    case 'Approved':
      return 'warn';
    case 'Received':
      return 'neutral';
    case 'Refunded':
      return 'good';
    case 'Rejected':
    case 'Cancelled':
      return 'bad';
  }
}

/**
 * A status as a pill: the word the caller translated, outlined in its tone. `sm`
 * is a table's (the backoffice's dense rows), `md` a page's heading (the
 * storefront's order) — the two apps' densities, ADR 0010.
 */
@Component({
  selector: 'lib-status-pill',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './status-pill.html',
  // The pill is the span inside; the host takes no box of its own, so a pill sits in a
  // table cell or a heading exactly as a bare span would.
  host: { class: 'contents' },
})
export class StatusPill {
  readonly tone = input.required<StatusTone>();
  readonly size = input<'sm' | 'md'>('sm');
}
