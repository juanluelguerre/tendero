import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';

/** Whether an order's money is in: captured, authorised or not paid — a word with its colour, never the colour alone. */
@Component({
  selector: 'backoffice-order-payment',
  imports: [TranslocoDirective],
  templateUrl: './order-payment.html',
  // Laid out by whoever places it: a table cell or the pane's head. The tags are the
  // shared ones (styles.css).
  host: { class: 'contents' },
})
export class OrderPayment {
  readonly order = input.required<OrderSummary>();
}
