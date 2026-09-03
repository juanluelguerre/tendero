import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, without } from '@tendero/shared-util';
import { OrderingService, type OrderMove } from '../../data-access/ordering.service';

/**
 * The orders, and the two buttons that move them.
 *
 * **The screen does not know the state machine, and that is on purpose.** Which
 * moves are legal comes from `Order.AllowedTransitions`, which lives on the
 * server and is the single source of truth; this table derives its buttons from
 * the status it was given, and when it gets one wrong the server answers 409
 * with a sentence and the row shows it. Reimplementing the table in TypeScript
 * is how the two start disagreeing, and the one that would be wrong is this one.
 *
 * Payment is two columns' worth of meaning in one: **authorised** and
 * **captured** are different facts, and a shopkeeper chasing a shipment that was
 * never paid for needs to tell them apart. Capturing happens on shipping, so a
 * shipped order that still says "Authorised" is exactly the row worth finding.
 */
@Component({
  selector: 'backoffice-orders',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orders-page.html',
  styleUrl: './orders-page.css',
})
export class OrdersPage {
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);

  protected readonly orders = signal<OrderSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly refusal = signal<Record<string, string>>({});

  /** Anything that is not finished. It is the count a shopkeeper opens the page
   * for. */
  protected readonly open = computed(
    () => this.orders().filter((order) => order.status !== 'Delivered' && order.status !== 'Cancelled').length,
  );

  /**
   * The moves this status allows, derived from the SERVER's status rather than
   * from a table kept here. When it is wrong the server says so, which is the
   * arrangement that keeps one table authoritative.
   */
  protected moves(order: OrderSummary): OrderMove[] {
    switch (order.status) {
      case 'Confirmed':
        return ['Ship', 'Cancel'];
      case 'Shipped':
        return ['Deliver'];
      case 'Pending':
      case 'PaymentAuthorized':
      case 'PaymentFailed':
        return ['Cancel'];
      default:
        return [];
    }
  }

  /** The one clay button in the row. If two are clay, neither is primary. */
  protected primary(order: OrderSummary): OrderMove | null {
    return this.moves(order)[0] ?? null;
  }

  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected money(order: OrderSummary): string {
    return formatPrice(order.total, order.currency, this.culture.active());
  }

  protected apply(order: OrderSummary, move: OrderMove): void {
    this.busy.set(order.orderId);
    this.refusal.update((current) => without(current, order.orderId));

    this.ordering.move(order.orderId, move).subscribe({
      next: (updated) => {
        this.orders.update((current) =>
          current.map((candidate) => (candidate.orderId === updated.orderId ? updated : candidate)),
        );
        this.busy.set(null);
      },
      error: (error: { status?: number; error?: unknown }) => {
        this.busy.set(null);
        this.refusal.update((current) => ({
          ...current,
          [order.orderId]:
            typeof error.error === 'string' ? error.error : 'The order refused that move.',
        }));
      },
    });
  }

  constructor() {
    this.ordering.orders().subscribe({
      next: (result) => {
        this.orders.set(result.orders);
        this.loading.set(false);
      },
      error: () => {
        this.failed.set(true);
        this.loading.set(false);
      },
    });
  }
}
