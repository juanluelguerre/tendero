import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';

/** Whether an order's money is in: captured, authorised or not paid — a word with its colour, never the colour alone. */
@Component({
  selector: 'backoffice-order-payment',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-payment.html',
  styleUrl: './order-payment.css',
})
export class OrderPayment {
  readonly order = input.required<OrderSummary>();
}
