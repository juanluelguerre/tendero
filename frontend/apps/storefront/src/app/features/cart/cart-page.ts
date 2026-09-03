import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AppliedDiscount, CartLineView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL, formatPrice } from '@tendero/shared-util';
import { CartStore } from '../../data-access/cart.service';

/**
 * The basket, and what it costs right now.
 *
 * Every figure on this page comes from a **live quote**, re-fetched after each
 * change. The cart itself carries no money (ADR 0016), which is why pressing "+"
 * makes two calls: one to change the basket and one to ask what it costs. At six
 * products that is free, and it is what keeps the shop from ever showing a total
 * nobody computed.
 *
 * The part worth looking at is the discount list: it shows **suppressed**
 * promotions too, with the reason. A discount that did not apply and a discount
 * of zero look identical in a total, and only one of them is something a shopper
 * can act on.
 */
@Component({
  selector: 'storefront-cart',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section *transloco="let t" class="cart">
      <header class="cart__head">
        <h1 class="cart__title">{{ t('cart.title') }}</h1>
        @if (!store.isEmpty()) {
          <span class="cart__count">{{ items(t) }}</span>
        }
      </header>

      @if (store.failed(); as problem) {
        <p class="notice notice--bad" role="alert">{{ t('cart.failed') }}</p>
      }

      @if (store.isEmpty()) {
        <div class="blank">
          <p class="blank__text">{{ t('cart.empty') }}</p>
          <p class="blank__hint">{{ t('cart.emptyHint') }}</p>
          <a class="button" routerLink="/">{{ t('nav.continueShopping') }}</a>
        </div>
      } @else {
        <div class="layout">
          <ul class="lines">
            @for (line of store.cart()!.lines; track line.sku) {
              <li class="line">
                @let source = imageUrl(line);
                @if (source) {
                  <img class="line__image" [src]="source" [alt]="line.productName" loading="lazy" />
                } @else {
                  <div class="line__image line__image--blank" aria-hidden="true">
                    {{ line.productName.charAt(0) }}
                  </div>
                }

                <div class="line__body">
                  <p class="line__name">{{ line.productName }}</p>
                  @if (line.variantLabel) {
                    <p class="line__variant">{{ line.variantLabel }}</p>
                  }
                  <p class="line__sku numeric">{{ line.sku }}</p>
                </div>

                <div class="stepper" role="group" [attr.aria-label]="t('cart.quantity', { name: line.productName })">
                  <button
                    type="button"
                    class="stepper__button"
                    [attr.aria-label]="t('cart.decrease')"
                    [disabled]="store.busy()"
                    (click)="change(line, -1)"
                  >−</button>
                  <span class="stepper__value numeric">{{ line.quantity }}</span>
                  <button
                    type="button"
                    class="stepper__button"
                    [attr.aria-label]="t('cart.increase')"
                    [disabled]="store.busy()"
                    (click)="change(line, 1)"
                  >+</button>
                </div>

                <p class="line__price numeric">{{ lineTotal(line.sku) }}</p>

                <button
                  type="button"
                  class="line__remove"
                  [attr.aria-label]="t('cart.remove', { name: line.productName })"
                  [disabled]="store.busy()"
                  (click)="remove(line)"
                >×</button>
              </li>
            }
          </ul>

          <aside class="summary">
            @if (store.quote(); as quote) {
              <dl class="totals">
                <div class="totals__row">
                  <dt>{{ t('cart.subtotal') }}</dt>
                  <dd class="numeric">{{ money(quote.subtotal) }}</dd>
                </div>

                @if (quote.discountTotal > 0) {
                  <div class="totals__row totals__row--off">
                    <dt>{{ t('cart.discounts') }}</dt>
                    <dd class="numeric">−{{ money(quote.discountTotal) }}</dd>
                  </div>
                }

                <div class="totals__row totals__row--muted">
                  <dt>{{ t('cart.shipping') }}</dt>
                  <dd>{{ t('cart.shippingAtCheckout') }}</dd>
                </div>

                <div class="totals__row totals__row--total">
                  <dt>{{ t('cart.total') }}</dt>
                  <dd class="numeric">{{ money(quote.total) }}</dd>
                </div>

                @if (quote.taxTotal > 0) {
                  <p class="totals__tax">{{ t('cart.tax') }} · {{ money(quote.taxTotal) }}</p>
                }
              </dl>

              <!-- The point of the screen. A suppressed promotion comes back
                   with its reason, and saying so is the difference between a
                   promotions engine and magic. -->
              @if (quote.discounts.length > 0) {
                <ul class="promos">
                  @for (discount of quote.discounts; track discount.promotionCode) {
                    <li class="promo" [class.promo--off]="discount.outcome !== 'Applied'">
                      <span class="promo__label">{{ discount.label }}</span>
                      @if (discount.outcome === 'Applied') {
                        <span class="promo__amount numeric">−{{ money(discount.amount) }}</span>
                      } @else {
                        <span class="promo__amount">{{ t('cart.suppressed') }}</span>
                      }
                      @if (discount.reason) {
                        <p class="promo__reason">{{ discount.reason }}</p>
                      }
                    </li>
                  }
                </ul>
              }
            } @else {
              <p class="muted">{{ store.busy() ? t('cart.loading') : t('cart.noQuote') }}</p>
            }

            <!-- The one clay action on the page (design/DESIGN.md). -->
            <a class="button button--block" routerLink="/checkout">{{ t('cart.checkout') }}</a>
            <a class="quiet" routerLink="/">{{ t('nav.continueShopping') }}</a>
          </aside>
        </div>
      }
    </section>
  `,
  styleUrl: './cart-page.css',
})
export class CartPage {
  protected readonly store = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Line net, from the quote — never multiplied on the client. */
  private readonly netBySku = computed(() =>
    new Map((this.store.quote()?.lines ?? []).map((line) => [line.sku, line.net])),
  );

  constructor() {
    void this.store.load();
  }

  protected items(t: (key: string, params?: Record<string, unknown>) => string): string {
    const count = this.store.itemCount();
    return count === 1 ? t('cart.itemsOne') : t('cart.itemsOther', { count });
  }

  protected imageUrl(line: CartLineView): string | null {
    return line.imageId ? `${this.baseUrl}/api/images/${line.imageId}` : null;
  }

  /**
   * What this line comes to, taken from the quote rather than computed here.
   * A client that multiplied price by quantity would disagree with the server
   * the first time a `BuyXGetY` promotion touched the line.
   */
  protected lineTotal(sku: string): string {
    const net = this.netBySku().get(sku);
    return net === undefined ? '—' : this.money(net);
  }

  protected money(amount: number): string {
    return formatPrice(amount, this.store.quote()?.currency ?? 'EUR', this.culture.active());
  }

  protected change(line: CartLineView, by: number): void {
    void this.store.setQuantity(line.sku, line.quantity + by);
  }

  protected remove(line: CartLineView): void {
    void this.store.remove(line.sku);
  }

  /** Kept so the template's `discount.outcome` narrows. */
  protected readonly applied: AppliedDiscount['outcome'] = 'Applied';
}
