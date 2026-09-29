import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';

/**
 * An order's status as the list and the pane both say it: the word, whether it
 * is a rehearsal or carried a mandate, and why it stopped. No state: what it
 * shows is the order it is given.
 */
@Component({
  selector: 'backoffice-order-status-label',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-status-label.html',
  styleUrl: './order-status-label.css',
})
export class OrderStatusLabel {
  readonly order = input.required<OrderSummary>();
}
