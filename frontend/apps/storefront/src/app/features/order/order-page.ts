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
  template: `
    <section *transloco="let t" class="order">
      @if (order(); as order) {
        <header class="order__head">
          @if (justPlaced()) {
            <p class="order__thanks">{{ t('order.placed') }}</p>
          }
          <h1 class="order__title">{{ t('order.reference', { id: shortId(order.orderId) }) }}</h1>
          <span class="chip" [class]="'chip--' + order.status">
            {{ t('order.status.' + order.status) }}
          </span>
        </header>

        <div class="layout">
          <div class="panels">
            <section class="panel">
              <h2 class="panel__title">{{ t('order.lines') }}</h2>
              <ul class="lines">
                @for (line of order.lines; track line.sku) {
                  <li class="line">
                    <span class="line__body">
                      <span class="line__name">{{ line.productName }}</span>
                      @if (line.variantLabel) {
                        <span class="line__variant">{{ line.variantLabel }}</span>
                      }
                      <span class="line__sku numeric">{{ line.sku }}</span>
                    </span>
                    <span class="line__quantity numeric">×{{ line.quantity }}</span>
                    <span class="line__price numeric">{{ money(line.unitPrice * line.quantity) }}</span>
                  </li>
                }
              </ul>
            </section>

            @if (canReturn()) {
              <section class="panel">
                <h2 class="panel__title">{{ t('order.returnTitle') }}</h2>
                <p class="panel__hint">{{ t('order.returnHint', { days: 14 }) }}</p>

                @if (returnMessage(); as message) {
                  <p class="notice" [class.notice--bad]="!returnOpened()"
                     [class.notice--good]="returnOpened()" role="status">{{ message }}</p>
                }

                <ul class="returnables">
                  @for (line of order.lines; track line.sku) {
                    <li class="returnable">
                      <label class="returnable__pick">
                        <input
                          type="checkbox"
                          [checked]="picked().has(line.sku)"
                          (change)="toggle(line)"
                        />
                        <span>{{ line.productName }}</span>
                      </label>

                      @if (picked().has(line.sku)) {
                        <div class="returnable__detail">
                          <label class="row">
                            <span class="row__label">{{ t('order.returnQuantity') }}</span>
                            <input
                              class="field field--tiny numeric"
                              type="number"
                              min="1"
                              [max]="line.quantity"
                              [ngModel]="quantityOf(line.sku)"
                              (ngModelChange)="setQuantity(line.sku, $event)"
                              [name]="'quantity-' + line.sku"
                            />
                          </label>

                          <label class="row row--grow">
                            <span class="row__label">{{ t('order.returnReason') }}</span>
                            <select
                              class="field"
                              [ngModel]="reasonOf(line.sku)"
                              (ngModelChange)="setReason(line.sku, $event)"
                              [name]="'reason-' + line.sku"
                            >
                              @for (reason of reasons; track reason) {
                                <option [value]="reason">{{ t('order.reason.' + reason) }}</option>
                              }
                            </select>
                          </label>
                        </div>
                      }
                    </li>
                  }
                </ul>

                <button
                  class="button"
                  type="button"
                  [disabled]="picked().size === 0 || sending()"
                  (click)="requestReturn()"
                >
                  {{ t('order.returnSubmit') }}
                </button>
              </section>
            } @else if (order.status !== 'Cancelled') {
              <p class="muted">{{ t('order.returnNotDelivered') }}</p>
            }

            @if (returns().length > 0) {
              <section class="panel">
                <h2 class="panel__title">{{ t('order.returns') }}</h2>
                <ul class="returns">
                  @for (request of returns(); track request.returnId) {
                    <li class="return">
                      <span class="chip chip--{{ request.status }}">
                        {{ t('order.returnStatus.' + request.status) }}
                      </span>
                      <span class="return__lines numeric">
                        {{ describe(request) }}
                      </span>
                      @if (request.refundAmount) {
                        <span class="return__refund numeric">{{ money(request.refundAmount) }}</span>
                      }
                      @if (request.resolution) {
                        <p class="return__resolution">{{ request.resolution }}</p>
                      }
                    </li>
                  }
                </ul>
              </section>
            }
          </div>

          <aside class="summary">
            <dl class="totals">
              <div class="totals__row">
                <dt>{{ t('cart.subtotal') }}</dt>
                <dd class="numeric">{{ money(order.subtotal) }}</dd>
              </div>

              @if (order.discountTotal > 0) {
                <div class="totals__row totals__row--off">
                  <dt>{{ t('cart.discounts') }}</dt>
                  <dd class="numeric">−{{ money(order.discountTotal) }}</dd>
                </div>
              }

              <div class="totals__row">
                <dt>{{ order.shippingLabel }}</dt>
                <dd class="numeric">{{ money(order.shipping) }}</dd>
              </div>

              <div class="totals__row totals__row--total">
                <dt>{{ t('cart.total') }}</dt>
                <dd class="numeric">{{ money(order.total) }}</dd>
              </div>

              @if (order.taxTotal > 0) {
                <p class="totals__tax">{{ t('cart.tax') }} · {{ money(order.taxTotal) }}</p>
              }
            </dl>

            <div class="ship">
              <p class="ship__label eyebrow">{{ t('order.shippingTo') }}</p>
              <p class="ship__value">{{ order.shippingAddress }}</p>
            </div>

            <a class="quiet" routerLink="/">{{ t('nav.continueShopping') }}</a>
          </aside>
        </div>
      } @else if (loading()) {
        <p class="muted" role="status" aria-live="polite">{{ t('order.loading') }}</p>
      } @else {
        <div class="blank">
          <p class="blank__text">{{ t('order.notFound') }}</p>
          <a class="button" routerLink="/">{{ t('nav.continueShopping') }}</a>
        </div>
      }
    </section>
  `,
  styleUrl: './order-page.css',
})
export class OrderPage {
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
