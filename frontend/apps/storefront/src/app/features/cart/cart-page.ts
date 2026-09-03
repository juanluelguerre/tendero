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
  templateUrl: './cart-page.html',
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
