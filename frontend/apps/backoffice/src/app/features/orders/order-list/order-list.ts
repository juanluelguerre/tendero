import { PricePipe } from '@tendero/shared-ui';
import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { orderReference } from '@tendero/shared-util';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { Find } from '../../../worklist/find/find';
import { ListState } from '../../../worklist/list-state/list-state';
import { Pager } from '../../../worklist/pager/pager';
import { placedAgo } from '../order-display';
import { primaryMove } from '../order-moves';
import { OrderPayment } from '../order-payment/order-payment';
import { OrderStatusLabel } from '../order-status-label/order-status-label';
import { OrdersStore } from '../orders.store';

/**
 * The orders, narrowed by status and by what is typed (R-4.2): a table from a
 * tablet up, cards on a phone, the likely move kept on each row. It reads and
 * acts through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-order-list',
  imports: [TranslocoDirective, FilterChips, Pager, OrderStatusLabel, OrderPayment, Find, ListState, PricePipe],
  templateUrl: './order-list.html',
})
export class OrderList {
  protected readonly store = inject(OrdersStore);
  protected readonly culture = inject(CultureStore);

  protected readonly pageSize = this.store.pageSize;
  protected readonly primaryMove = primaryMove;
  protected readonly shortId = orderReference;

  protected placed(iso: string): string {
    return placedAgo(iso, this.culture.active());
  }
}
