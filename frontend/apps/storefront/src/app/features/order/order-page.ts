import { HttpClient, HttpParams } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { OrderDetail, OrderLineView, OrderView, ReturnView } from '@tendero/shared-api';
import { RETURN_REASONS } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL, formatPrice } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';
import { ShopLinks } from '../../shop-links';

/**
 * The order, and the way back.
 *
 * It is the confirmation page and the returns page at once, because they are the
 * same page at two different moments — and a shop that made you find a second
 * screen to send something back is a shop that makes you phone it instead.
 *
 * The return form only appears once the order is **Delivered**. That is not a UI
 * preference: `ReturnRequest.Open` refuses anything else, and offering a form
 * the server will reject is how an interface teaches people not to trust it.
 */
@Component({
  selector: 'storefront-order',
  imports: [TranslocoDirective, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './order-page.html',
  styleUrl: './order-page.css',
})
export class OrderPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly reasons = RETURN_REASONS;

  protected readonly order = signal<OrderView | null>(null);
  protected readonly returns = signal<ReturnView[]>([]);
  protected readonly loading = signal(true);
  protected readonly sending = signal(false);
  protected readonly returnMessage = signal<string | null>(null);
  protected readonly returnOpened = signal(false);

  /** Which lines are coming back, and on what terms. */
  protected readonly picked = signal(new Map<string, { quantity: number; reason: string }>());

  /**
   * True only when this page was reached from checkout. The "thank you" belongs
   * to the moment, not to the URL: coming back to an order a week later should
   * not be congratulated for it.
   */
  protected readonly justPlaced = signal(false);

  constructor() {
    const state = this.router.getCurrentNavigation()?.extras.state as { order?: OrderView } | undefined;

    if (state?.order) {
      // Checkout already has the order; asking for it again would be a round
      // trip to learn what we were just told.
      this.order.set(state.order);
      this.justPlaced.set(true);
      this.loading.set(false);
    }

    // Loaded anyway, and not only when the navigation state is empty. A refresh
    // has to work — a confirmation page that vanishes on F5 is a page people
    // screenshot instead — and the returns against the order only exist server
    // side.
    void this.load();
  }

  protected money(amount: number): string {
    return formatPrice(amount, this.order()?.currency ?? 'EUR', this.culture.active());
  }

  /** The first segment. A full GUID is unreadable and the URL has the rest. */
  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected canReturn(): boolean {
    return this.order()?.status === 'Delivered';
  }

  protected quantityOf(sku: string): number {
    return this.picked().get(sku)?.quantity ?? 1;
  }

  protected reasonOf(sku: string): string {
    return this.picked().get(sku)?.reason ?? RETURN_REASONS[0];
  }

  protected toggle(line: OrderLineView): void {
    this.picked.update((current) => {
      const next = new Map(current);

      if (next.has(line.sku)) next.delete(line.sku);
      else next.set(line.sku, { quantity: 1, reason: RETURN_REASONS[0] });

      return next;
    });
  }

  protected setQuantity(sku: string, quantity: number): void {
    this.picked.update((current) => {
      const next = new Map(current);
      const line = next.get(sku);

      if (line) next.set(sku, { ...line, quantity: Math.max(1, Number(quantity) || 1) });

      return next;
    });
  }

  protected setReason(sku: string, reason: string): void {
    this.picked.update((current) => {
      const next = new Map(current);
      const line = next.get(sku);

      if (line) next.set(sku, { ...line, reason });

      return next;
    });
  }

  protected describe(request: ReturnView): string {
    return request.lines.map((line) => `${line.sku} ×${line.quantity}`).join(' · ');
  }

  protected async requestReturn(): Promise<void> {
    const order = this.order();
    if (!order) return;

    this.sending.set(true);
    this.returnMessage.set(null);

    try {
      const opened = await firstValueFrom(
        this.http.post<ReturnView>(
          `${this.baseUrl}/api/orders/${order.orderId}/returns`,
          {
            lines: [...this.picked().entries()].map(([sku, line]) => ({
              sku,
              quantity: line.quantity,
              reason: line.reason,
              comment: null,
            })),
          },
          { params: new HttpParams().set('culture', this.culture.active()) },
        ),
      );

      this.returns.update((current) => [opened, ...current]);
      this.picked.set(new Map());
      this.returnOpened.set(true);
      this.returnMessage.set(this.transloco.translate('order.returnOpened'));
    } catch (error) {
      const failure = error as { status?: number; error?: unknown };

      this.returnOpened.set(false);
      this.returnMessage.set(
        failure.status === 409
          ? this.transloco.translate('order.returnClosed')
          : this.transloco.translate('order.returnFailed', {
              reason: typeof failure.error === 'string' ? failure.error : '',
            }),
      );
    } finally {
      this.sending.set(false);
    }
  }

  private async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');

    if (!id) {
      this.loading.set(false);
      return;
    }

    try {
      const detail = await firstValueFrom(
        this.http.get<OrderDetail>(`${this.baseUrl}/api/orders/${id}`, {
          params: new HttpParams().set('culture', this.culture.active()),
        }),
      );

      this.order.set(detail.order);
      this.returns.set(detail.returns);
    } catch {
      // A 404 is the honest answer to a URL somebody typed wrong, and the
      // template says so. Only the freshly-placed case has an order to keep.
      if (!this.justPlaced()) this.order.set(null);
    } finally {
      this.loading.set(false);
    }
  }
}
