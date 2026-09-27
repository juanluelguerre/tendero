import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  type ElementRef,
  inject,
  Injector,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, without } from '@tendero/shared-util';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { OrderingService, type OrderMove } from '../../data-access/ordering.service';
import { BackofficeUsage, type BackofficeAction } from '../../usage';

/** Each move as the usage event names it (ADR 0041). */
const OrderActions = { Ship: 'order_ship', Deliver: 'order_deliver', Cancel: 'order_cancel' } as const satisfies Record<OrderMove, BackofficeAction>;

/** The filter's statuses in the order a working day meets them: what is to ship first. */
const Statuses = ['Confirmed', 'Pending', 'PaymentAuthorized', 'PaymentFailed', 'Shipped', 'Delivered', 'Cancelled'] as const;

/** The status the list opens on: the job this screen is for is shipping (R-0, B3). */
const ToShip = 'Confirmed';

/**
 * The orders, as R-3 drew them (R-4.2): a list narrowed by status and by what is
 * typed, the likely move kept on the row, and a pane beside the list with what
 * the decision needs, where cancelling lives apart and asks first (R-1, B-F3,
 * B-F4, B-F5). The filter, the text and the open order are in the URL, so a
 * dashboard figure or the palette can open the list exactly there.
 *
 * **The screen does not know the state machine, and that is on purpose.** Which
 * moves are legal comes from `Order.AllowedTransitions` on the server; the
 * buttons are derived from the status it sent, and when one is wrong the server
 * answers 409 with a sentence that the screen shows as it came.
 */
@Component({
  selector: 'backoffice-orders',
  imports: [TranslocoDirective, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orders-page.html',
  styleUrl: './orders-page.css',
  host: { '(keydown.escape)': 'closeOnEscape()' },
})
export class OrdersPage {
  private readonly ordering = inject(OrderingService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);

  private readonly paneHeading = viewChild<ElementRef<HTMLElement>>('paneHeading');
  private readonly cancelDialog = viewChild<ElementRef<HTMLDialogElement>>('cancelDialog');

  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  /** The status in the URL; none means the job's default, `all` means no filter. */
  protected readonly statusKey = computed(() => this.params().get('status') ?? ToShip);
  private readonly status = computed(() => (this.statusKey() === 'all' ? null : this.statusKey()));
  protected readonly selectedId = computed(() => this.params().get('order'));

  protected readonly text = signal(this.route.snapshot.queryParamMap.get('q') ?? '');
  private readonly query = toSignal(toObservable(this.text).pipe(debounceTime(250), distinctUntilChanged()), {
    initialValue: this.text(),
  });

  private readonly list = rxResource({
    params: () => ({ status: this.status(), text: this.query() }),
    stream: ({ params }) => this.ordering.orders(params.status, params.text),
  });

  /** The rows, replaced one by one as moves come back, until the list is asked again. */
  protected readonly orders = linkedSignal(() => this.list.value()?.orders ?? []);
  protected readonly loading = computed(() => this.list.isLoading() && !this.list.hasValue());
  protected readonly failed = computed(() => this.list.status() === 'error');

  /** One chip per status that has orders, plus the one showing and the job's own. */
  protected readonly filters = computed(() => {
    const counts = new Map((this.list.value()?.counts ?? []).map((entry) => [entry.status, entry.count]));
    const shown = Statuses.filter((status) => status === ToShip || status === this.statusKey() || (counts.get(status) ?? 0) > 0);
    const total = [...counts.values()].reduce((sum, count) => sum + count, 0);
    return [...shown.map((status) => ({ key: status as string, count: counts.get(status) ?? 0 })), { key: 'all', count: total }];
  });

  /** The open order's row, kept while a move takes it out of the filtered list. */
  protected readonly selected = linkedSignal<OrderSummary | null, OrderSummary | null>({
    source: () => this.orders().find((order) => order.orderId === this.selectedId()) ?? null,
    computation: (row, previous) =>
      row ?? (previous?.value?.orderId === this.selectedId() ? previous.value : null),
  });

  protected readonly detail = rxResource({
    params: () => {
      const id = this.selectedId();
      return id ? { id, culture: this.culture.active() } : undefined;
    },
    stream: ({ params }) => this.ordering.order(params.id, params.culture),
  });

  protected readonly busy = signal<string | null>(null);
  protected readonly refusal = signal<Record<string, string>>({});
  protected readonly cancelReason = signal('');

  /** The likely move for a status: what the row and the pane lead with. Cancelling is never it. */
  protected primaryMove(status: string): OrderMove | null {
    switch (status) {
      case 'Confirmed':
        return 'Ship';
      case 'Shipped':
        return 'Deliver';
      default:
        return null;
    }
  }

  /** Whether the server's table would let this status be cancelled; it still has the last word. */
  protected cancellable(status: string): boolean {
    return ['Pending', 'PaymentAuthorized', 'PaymentFailed', 'Confirmed'].includes(status);
  }

  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  /** "2 days ago", in the reader's language. */
  protected placed(iso: string): string {
    const minutes = Math.round((Date.parse(iso) - Date.now()) / 60_000);
    const format = new Intl.RelativeTimeFormat(this.culture.active(), { numeric: 'auto', style: 'short' });
    if (Math.abs(minutes) < 60) return format.format(minutes, 'minute');
    if (Math.abs(minutes) < 60 * 24) return format.format(Math.round(minutes / 60), 'hour');
    return format.format(Math.round(minutes / (60 * 24)), 'day');
  }

  protected filterBy(status: string): void {
    void this.router.navigate([], {
      queryParams: { status: status === ToShip ? null : status },
      queryParamsHandling: 'merge',
    });
  }

  protected open(order: OrderSummary): void {
    void this.router.navigate([], { queryParams: { order: order.orderId }, queryParamsHandling: 'merge' });
    // The pane's heading takes the focus, so a keyboard or a screen reader is
    // where the eye went.
    afterNextRender(() => this.paneHeading()?.nativeElement.focus(), { injector: this.injector });
  }

  protected close(): void {
    const id = this.selectedId();
    void this.router.navigate([], { queryParams: { order: null }, queryParamsHandling: 'merge' });
    afterNextRender(() => document.getElementById(`open-${id}`)?.focus(), { injector: this.injector });
  }

  protected closeOnEscape(): void {
    if (this.selectedId() && !this.cancelDialog()?.nativeElement.open) this.close();
  }

  protected askToCancel(): void {
    this.cancelReason.set('');
    this.cancelDialog()?.nativeElement.showModal();
  }

  protected confirmCancel(order: OrderSummary): void {
    this.cancelDialog()?.nativeElement.close();
    this.apply(order, 'Cancel', this.cancelReason().trim() || undefined);
  }

  protected apply(order: OrderSummary, move: OrderMove, reason?: string): void {
    this.busy.set(order.orderId);
    this.refusal.update((current) => without(current, order.orderId));

    this.ordering
      .move(order.orderId, move, reason)
      .pipe(this.usage.reported('orders', OrderActions[move]))
      .subscribe({
        next: (updated) => {
          this.orders.update((current) => current.map((row) => (row.orderId === updated.orderId ? updated : row)));
          this.selected.update((row) => (row?.orderId === updated.orderId ? updated : row));
          this.busy.set(null);
          if (this.selectedId() === updated.orderId) this.detail.reload();
        },
        error: (error: { error?: unknown }) => {
          this.busy.set(null);
          this.refusal.update((current) => ({
            ...current,
            [order.orderId]: typeof error.error === 'string' ? error.error : '',
          }));
        },
      });
  }
}
