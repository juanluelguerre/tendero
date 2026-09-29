import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { Pager } from '../../../worklist/pager/pager';
import { placedAgo, shortId } from '../order-display';
import { primaryMove } from '../order-moves';
import { OrderPayment } from '../order-payment/order-payment';
import { OrderStatusLabel } from '../order-status-label/order-status-label';
import { OrdersStore, PageSize } from '../orders.store';

/**
 * The orders, narrowed by status and by what is typed (R-4.2): a table from a
 * tablet up, cards on a phone, the likely move kept on each row. It reads and
 * acts through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-order-list',
  imports: [TranslocoDirective, FilterChips, Pager, OrderStatusLabel, OrderPayment],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-list.html',
  styleUrl: './order-list.css',
})
export class OrderList {
  protected readonly store = inject(OrdersStore);
  private readonly culture = inject(CultureStore);

  protected readonly pageSize = PageSize;
  protected readonly primaryMove = primaryMove;
  protected readonly shortId = shortId;

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  protected placed(iso: string): string {
    return placedAgo(iso, this.culture.active());
  }
}
