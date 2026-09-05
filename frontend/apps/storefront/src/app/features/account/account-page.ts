import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductImagePlaceholder } from '../../product-image-placeholder';
import { RouterLink } from '@angular/router';
import type { OrderSummary } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime, formatPrice, ProductImageUrls } from '@tendero/shared-util';
import { AccountService } from '../../data-access/account.service';
import { CartStore } from '../../data-access/cart.service';
import { ShopLinks } from '../../shop-links';

type PageState =
  | { status: 'signedOut' }
  | { status: 'signingIn' }
  | { status: 'signedIn'; orders: OrderSummary[] }
  | { status: 'failed' };

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
 * basket they were already carrying.
 */
@Component({
  selector: 'storefront-account-page',
  imports: [TranslocoDirective, RouterLink, ProductImagePlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-page.html',
  styleUrl: './account-page.css',
})
export class AccountPage {
  private readonly auth = inject(AuthStore);
  private readonly accounts = inject(AccountService);
  private readonly cart = inject(CartStore);
  private readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  protected readonly links = inject(ShopLinks);
  protected readonly identity = this.auth.identity;
  protected readonly state = signal<PageState>({ status: 'signingIn' });

  constructor() {
    void this.start();
  }

  private async start(): Promise<void> {
    if (this.auth.isSignedIn()) {
      try {
        await this.attach();
      } catch {
        this.state.set({ status: 'failed' });
        return;
      }

      this.loadOrders();
      return;
    }

    this.state.set({ status: 'signedOut' });
  }

  /** Hands the browser to the issuer. Everything after this happens on the way back. */
  protected signIn(): void {
    this.state.set({ status: 'signingIn' });
    this.auth.signIn();
  }

  /**
   * What the shop still owes after the redirect, and the order matters.
   *
   * The CUSTOMER first — a cart cannot be attached to somebody who does not
   * exist yet — and only then the basket. The two calls are two contexts:
   * `Accounts` says who you are, `Ordering` owns the cart, and neither imports
   * the other.
   *
   * It runs on every load of this page while signed in, which is safe because
   * both operations are idempotent: linking an identity that is already linked
   * reports `recognised` and writes nothing, and a cart that is already claimed
   * is the same cart.
   */
  private async attach(): Promise<void> {
    const name = this.auth.identity()?.name ?? '';

    await this.accounts.link(name, this.culture.active());

    // The basket, if there is one. Best-effort on purpose: a cart that belongs
    // to another account comes back 409, and refusing to sign somebody in over a
    // basket they were not carrying would be absurd.
    const token = this.cart.token;
    if (!token) return;

    try {
      await this.accounts.claimCart(token);
      await this.cart.load();
    } catch {
      // Not this account's basket. Signing in still succeeded.
    }
  }

  /** Ends the session at the issuer too, which is what makes it look ended. */
  protected signOut(): void {
    this.state.set({ status: 'signedOut' });
    this.auth.signOut();
  }

  /**
   * The images that did not load, keyed by SKU.
   *
   * The seed points at pictures that may not be there, so a broken image is the
   * DEFAULT path on a fresh clone rather than an error — the same reason the
   * search card carries this and swaps in a labelled tile.
   */
  protected readonly broken = signal(new Set<string>());

  protected markBroken(sku: string): void {
    this.broken.update((current) => new Set(current).add(sku));
  }

  /** Whether this order has anything to show, as opposed to boxes to draw. */
  protected hasImages(order: OrderSummary): boolean {
    return order.preview.some((line) => line.imageId && !this.broken().has(line.sku));
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

  private loadOrders(): void {
    this.accounts.orders().subscribe({
      next: (result) => this.state.set({ status: 'signedIn', orders: result.orders }),
      error: () => this.state.set({ status: 'failed' }),
    });
  }
}
