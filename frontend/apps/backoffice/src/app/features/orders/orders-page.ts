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
  template: `
    <section *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('orders.title') }}</h1>
        @if (open() > 0) {
          <span class="tag tag--warn">{{ t('orders.openCount', { count: open() }) }}</span>
        }
      </header>
      <p class="page-hint">{{ t('orders.hint') }}</p>

      @if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('orders.loading') }}</p>
      } @else if (failed()) {
        <p class="failed" role="alert">{{ t('orders.failed') }}</p>
      } @else if (orders().length === 0) {
        <div class="empty"><p class="empty__text">{{ t('orders.empty') }}</p></div>
      } @else {
        <div class="table-wrap">
          <table class="table">
            <caption class="sr-only">{{ t('orders.title') }}</caption>
            <thead>
              <tr>
                <th scope="col">{{ t('orders.column.order') }}</th>
                <th scope="col">{{ t('orders.column.customer') }}</th>
                <th scope="col">{{ t('orders.column.status') }}</th>
                <th scope="col">{{ t('orders.column.payment') }}</th>
                <th scope="col" class="numeric">{{ t('orders.column.lines') }}</th>
                <th scope="col" class="numeric">{{ t('orders.column.total') }}</th>
                <th scope="col"><span class="sr-only">{{ t('orders.column.action') }}</span></th>
              </tr>
            </thead>
            <tbody>
              @for (order of orders(); track order.orderId) {
                <tr>
                  <td class="numeric code" [title]="order.orderId">{{ shortId(order.orderId) }}</td>
                  <td>
                    {{ order.recipientName }}
                    <span class="city">{{ order.city }}</span>
                  </td>
                  <td>
                    <span class="status" [class]="'status--' + order.status">
                      {{ t('orders.status.' + order.status) }}
                    </span>
                  </td>
                  <td>
                    @if (order.isCaptured) {
                      <span class="tag tag--ok">{{ t('orders.captured') }}</span>
                    } @else if (order.isPaid) {
                      <span class="tag tag--warn">{{ t('orders.paid') }}</span>
                    } @else {
                      <span class="tag tag--danger">{{ t('orders.unpaid') }}</span>
                    }
                  </td>
                  <td class="numeric">{{ order.lineCount }}</td>
                  <td class="numeric total">{{ money(order) }}</td>
                  <td class="actions">
                    @for (move of moves(order); track move) {
                      <button
                        class="button button--small"
                        [class.button--quiet]="move !== primary(order)"
                        type="button"
                        [disabled]="busy() === order.orderId"
                        (click)="apply(order, move)"
                      >
                        {{ busy() === order.orderId ? t('orders.moving') : t('orders.move.' + move) }}
                      </button>
                    }
                  </td>
                </tr>

                @if (refusal()[order.orderId]; as reason) {
                  <tr class="refusal">
                    <td colspan="7">{{ reason }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    .code { color: var(--text); font-weight: 500; }
    .city { display: block; color: var(--text-subtle); font-size: var(--text-3xs); }
    .total { font-weight: 600; }
    .actions { text-align: end; white-space: nowrap; }
    .actions .button + .button { margin-inline-start: var(--space-2); }

    /* Colour never carries meaning alone: every one of these has the word in
       it, and the colour only makes the word findable in a column. */
    .status {
      display: inline-block;
      padding: 1px var(--space-2);
      border: 1px solid currentColor;
      border-radius: var(--radius-sm);
      font-size: var(--text-3xs);
      white-space: nowrap;
    }

    .status--Pending { color: var(--text-subtle); }
    .status--PaymentAuthorized, .status--Confirmed { color: var(--warning); }
    .status--Shipped { color: var(--accent); }
    .status--Delivered { color: var(--positive); }
    .status--PaymentFailed, .status--Cancelled { color: var(--danger); }

    /* The server's own sentence, under the row it belongs to. A toast would
       float away from the thing it is about. */
    .refusal td {
      padding-block: var(--space-2);
      color: var(--danger);
      font-size: var(--text-xs);
    }
  `,
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
