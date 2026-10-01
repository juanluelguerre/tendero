import { afterNextRender, Component, inject, Injector } from '@angular/core';
import { OrderList } from './order-list/order-list';
import { OrderPane } from './order-pane/order-pane';
import { OrdersStore } from './orders.store';

/**
 * The orders, as R-3 drew them (R-4.2): the list and, beside it, the open
 * order's pane — two components on one store (ADR 0047), provided here so it
 * lives and dies with the page.
 *
 * **The screen does not know the state machine, and that is on purpose.** Which
 * moves are legal comes from `Order.AllowedTransitions` on the server; the
 * buttons are derived from the status it sent, and when one is wrong the server
 * answers 409 with a sentence that the screen shows as it came.
 */
@Component({
  selector: 'backoffice-orders',
  imports: [OrderList, OrderPane],
  templateUrl: './orders-page.html',
  // The page is a frame: the list-and-pane layout is shared by every list of work
  // (styles.css), and what the list and the pane have of their own is in their folders.
  host: { class: 'block' },
  providers: [OrdersStore],
})
export class OrdersPage {
  protected readonly store = inject(OrdersStore);
  private readonly injector = inject(Injector);

  /** Closing the pane puts the keyboard back on the row that opened it. */
  protected close(): void {
    const id = this.store.selectedId();
    this.store.close();
    afterNextRender(() => document.getElementById(`open-${id}`)?.focus(), { injector: this.injector });
  }
}
