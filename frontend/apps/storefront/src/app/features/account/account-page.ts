import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { RouterLink } from '@angular/router';
import type { OrderSummary } from '@tendero/shared-api';
import { AuthStore, type TenderoIdentity } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime, formatPrice } from '@tendero/shared-util';
import { AccountService } from '../../data-access/account.service';
import { CartStore } from '../../data-access/cart.service';
import { ShopLinks } from '../../shop-links';

type PageState =
  | { status: 'signedOut'; identities: TenderoIdentity[] }
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
 * The identity picker is the development issuer's, and it is the storefront's
 * OWN screen rather than a shared one (ADR 0010): the backoffice picks between
 * three seeded staff in a dense dark bar, and a shopper will one day type an
 * email. Sharing the view would give a component with a variant matrix.
 */
@Component({
  selector: 'storefront-account-page',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account-page.html',
  styleUrl: './account-page.css',
})
export class AccountPage {
  private readonly auth = inject(AuthStore);
  private readonly accounts = inject(AccountService);
  private readonly cart = inject(CartStore);
  private readonly culture = inject(CultureStore);

  protected readonly links = inject(ShopLinks);
  protected readonly identity = this.auth.identity;
  protected readonly state = signal<PageState>({ status: 'signingIn' });

  constructor() {
    void this.start();
  }

  private async start(): Promise<void> {
    if (this.auth.isSignedIn()) {
      this.loadOrders();
      return;
    }

    this.state.set({ status: 'signedOut', identities: await this.auth.identities() });
  }

  /**
   * Signing in is three steps and the order matters.
   *
   * The token first, then the CUSTOMER — because a cart cannot be attached to
   * somebody who does not exist yet — and only then the basket. The two API
   * calls are two contexts: `Accounts` says who you are, `Ordering` owns the
   * cart, and neither imports the other.
   */
  protected async use(identity: TenderoIdentity): Promise<void> {
    this.state.set({ status: 'signingIn' });

    try {
      await this.auth.signIn(identity);
      await this.accounts.link(identity.name, this.culture.active());

      // The basket, if there is one. It is best-effort on purpose: a cart that
      // belongs to another account comes back 409, and refusing to sign
      // somebody in over a basket they were not carrying would be absurd.
      const token = this.cart.token;
      if (token) {
        try {
          await this.accounts.claimCart(token);
          await this.cart.load();
        } catch {
          // Not this account's basket. Signing in still succeeded.
        }
      }

      this.loadOrders();
    } catch {
      this.state.set({ status: 'failed' });
    }
  }

  protected async signOut(): Promise<void> {
    this.auth.signOut();
    this.state.set({ status: 'signedOut', identities: await this.auth.identities() });
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
