import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AppliedDiscount, CartLineView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { CartStore } from '../../cart/cart.store';
import { discountReason } from '../../cart/discount-reason';
import { canAddOneMore, stockNote } from '../../cart/stock-note';
import { TaxonomyStore } from '../../taxonomy/taxonomy.store';
import { ShopLinks } from '../../shop-links';
import { Icon, Notice, PricePipe } from '@tendero/shared-ui';
import { Totals } from '../../totals/totals';
import { ProductImage } from '../../product-image/product-image';
import { Summary } from '../../summary/summary';

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
  imports: [TranslocoDirective, RouterLink, Notice, Totals, ProductImage, Icon, Summary, PricePipe],
  templateUrl: './cart-page.html',
})
export class CartPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  protected readonly store = inject(CartStore);
  protected readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);
  private readonly taxonomy = inject(TaxonomyStore);

  /** Line net, from the quote — never multiplied on the client. */
  /**
   * What each line comes to, taken from the quote rather than computed here.
   * A client that multiplied price by quantity would disagree with the server
   * the first time a `BuyXGetY` promotion touched the line.
   */
  protected readonly netBySku = computed(
    () => new Map((this.store.quote()?.lines ?? []).map((line) => [line.sku, line.net])),
  );

  constructor() {
    void this.store.load();
  }

  protected items(t: (key: string, params?: Record<string, unknown>) => string): string {
    const count = this.store.itemCount();
    return count === 1 ? t('cart.itemsOne') : t('cart.itemsOther', { count });
  }

  protected imageUrl(line: CartLineView): string | null {
    return line.imageId ? this.images.of(line.imageId) : null;
  }

  protected change(line: CartLineView, by: number): void {
    void this.store.setQuantity(line.sku, line.quantity + by);
  }

  protected remove(line: CartLineView): void {
    void this.store.remove(line.sku);
  }

  /**
   * Why a promotion did not apply, in this page's words: money the reader's
   * way, a category by the name the menu gives it, an article by the name the
   * basket gives it (`discountReason`).
   */
  protected reason(discount: AppliedDiscount, t: (key: string, params?: Record<string, unknown>) => string) {
    return discountReason(discount, {
      t,
      money: (amount) => formatPrice(amount, this.culture.active()),
      category: (code) => this.taxonomy.categories().find((category) => category.code === code)?.name ?? null,
      product: (sku) => this.store.cart()?.lines.find((line) => line.sku === sku)?.productName ?? null,
    });
  }

  /** What the line says about stock, or nothing; and whether "+" asks for something there is. */
  protected readonly stockNote = stockNote;
  protected readonly canAddOneMore = canAddOneMore;

  /** Kept so the template's `discount.outcome` narrows. */
  protected readonly applied: AppliedDiscount['outcome'] = 'Applied';
}
