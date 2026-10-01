import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { OrderSummary } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime, formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { ProductImage } from '../../product-image/product-image';
import { ShopLinks } from '../../shop-links';
import { TrailHistory } from '../../trail/trail-history/trail-history';
import { AccountStore } from './account.store';
import { PageState } from '../../page-state/page-state';

/**
 * A shopper's account, and their orders.
 *
 * **The shop works without it**, and that is the point rather than a caveat: a
 * guest fills a basket, is quoted live, pays and gets an order — the cart's
 * 256-bit token is what makes that possible, and nothing on this page is a
 * prerequisite for buying anything.
 *
 * What signing in adds is a NAME on the orders that already existed. Phase 5
 * left a comment saying an order id was the credential "until phase 7 puts an
 * account behind it"; this is that.
 *
 * **Signing in is a redirect now**, so this page no longer lists who exists or
 * handles a password — the issuer does both. What is left here is the half that
 * is genuinely the shop's: attaching the person to a customer, and attaching the
 * basket they were already carrying — its store's (ADR 0047); the page
 * presents the orders.
 */
@Component({
  selector: 'storefront-account-page',
  imports: [TranslocoDirective, RouterLink, ProductImage, TrailHistory, PageState],
  providers: [AccountStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-page.html',
  styleUrl: './account-page.css',
})
export class AccountPage {
  protected readonly store = inject(AccountStore);
  protected readonly links = inject(ShopLinks);
  protected readonly identity = inject(AuthStore).identity;

  private readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  constructor() {
    void this.store.start();
  }

  /** Whether this order has anything to show, as opposed to boxes to draw. */
  protected hasImages(order: OrderSummary): boolean {
    return order.preview.some((line) => line.imageId && !this.store.broken().has(line.sku));
  }

  protected imageUrl(imageId: string): string {
    return this.images.of(imageId);
  }

  /**
   * The first few things in the order, as a sentence.
   *
   * It ends in an ellipsis only when there is more, because "Coffee maker…" on
   * an order of exactly one thing reads like something failed to load.
   */
  protected names(order: OrderSummary): string {
    const shown = order.preview.map((line) => line.name).join(', ');
    return order.lineCount > order.preview.length ? `${shown}…` : shown;
  }

  protected price(order: OrderSummary): string {
    return formatPrice(order.total, order.currency, this.culture.active());
  }

  protected when(order: OrderSummary): string {
    return formatDateTime(order.createdAt, this.culture.active());
  }
}
