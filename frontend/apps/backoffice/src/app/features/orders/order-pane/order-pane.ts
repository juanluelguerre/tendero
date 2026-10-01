import { Component, inject, output, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { CopyText } from '@tendero/shared-ui';
import { formatPrice, orderReference, valueOf } from '@tendero/shared-util';
import { ConfirmDialog } from '../../../worklist/confirm-dialog/confirm-dialog';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { cancellable, primaryMove } from '../order-moves';
import { OrderPayment } from '../order-payment/order-payment';
import { OrderStatusLabel } from '../order-status-label/order-status-label';
import { OrdersStore } from '../orders.store';
import { OrderPaneStore } from './order-pane.store';

/**
 * The open order beside the list (R-1, B-F3 to B-F5): what the decision needs,
 * the view's one clay action, and cancelling apart at its foot, asking first.
 * It reads and acts through the page's store, which the list shares.
 */
@Component({
  selector: 'backoffice-order-pane',
  imports: [TranslocoDirective, WorklistPane, ConfirmDialog, OrderStatusLabel, OrderPayment, CopyText],
  templateUrl: './order-pane.html',
  // The pane is laid out by the page's list-and-pane grid, as if it were its child; the
  // pane itself is shared (worklist/).
  host: { class: 'contents' },
  providers: [OrderPaneStore],
})
export class OrderPane {
  /** A resource read without throwing when its request failed (Angular 22). */
  protected readonly valueOf = valueOf;

  protected readonly store = inject(OrdersStore);
  /** The pane's own: the open order's detail. */
  protected readonly pane = inject(OrderPaneStore);
  private readonly culture = inject(CultureStore);
  private readonly cancelDialog = viewChild(ConfirmDialog);

  /** The person closed the pane; the page puts the keyboard back on the row that opened it. */
  readonly closed = output<void>();

  protected readonly primaryMove = primaryMove;
  protected readonly cancellable = cancellable;
  protected readonly shortId = orderReference;

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  protected askToCancel(): void {
    this.cancelDialog()?.open();
  }
}
