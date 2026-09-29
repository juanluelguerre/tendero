import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import type { OrderSummary } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { firstValueFrom } from 'rxjs';
import { CartStore } from '../../cart/cart.store';
import { AccountService } from '../../data-access/account.service';
import { CustomerLink } from '../../data-access/customer-link';

export type AccountPageState =
  | { status: 'signedOut' }
  | { status: 'signingIn' }
  | { status: 'signedIn'; orders: OrderSummary[] }
  | { status: 'failed' };

interface AccountState {
  readonly state: AccountPageState;
  /**
   * The images that did not load, keyed by SKU.
   *
   * The seed points at pictures that may not be there, so a broken image is the
   * DEFAULT path on a fresh clone rather than an error — the same reason the
   * search card carries this and swaps in a labelled tile.
   */
  readonly broken: ReadonlySet<string>;
}

/**
 * The account page's own state (ADR 0047): who the shopper is to the shop, the
 * basket they carried in, and their orders.
 *
 * **Signing in is a redirect**, so nothing here lists who exists or handles a
 * password — the issuer does both. What is left is the half that is genuinely
 * the shop's: attaching the person to a customer, and attaching the basket they
 * were already carrying.
 */
export const AccountStore = signalStore(
  withState<AccountState>({ state: { status: 'signingIn' }, broken: new Set<string>() }),
  withProps(() => ({
    _auth: inject(AuthStore),
    _accounts: inject(AccountService),
    _cart: inject(CartStore),
    _customer: inject(CustomerLink),
  })),
  withMethods((store) => {
    /**
     * What the shop still owes after the redirect, and the order matters.
     *
     * The CUSTOMER first — a cart cannot be attached to somebody who does not
     * exist yet — and only then the basket. The two calls are two contexts:
     * `Accounts` says who you are, `Ordering` owns the cart, and neither imports
     * the other.
     *
     * It runs on every load of the page while signed in, which is safe because
     * both operations are idempotent: linking an identity that is already linked
     * reports `recognised` and writes nothing, and a cart that is already claimed
     * is the same cart. The link is `CustomerLink`'s, shared with the trail's
     * merge at sign-in (R-8), so the tab asks it once.
     */
    const attach = async (): Promise<void> => {
      await store._customer.ensure();

      // The basket, if there is one. Best-effort on purpose: a cart that belongs
      // to another account comes back 409, and refusing to sign somebody in over
      // a basket they were not carrying would be absurd.
      const token = store._cart.token();
      if (!token) return;
      try {
        await store._accounts.claimCart(token);
        await store._cart.load();
      } catch {
        // Not this account's basket. Signing in still succeeded.
      }
    };

    return {
      async start(): Promise<void> {
        if (!store._auth.isSignedIn()) {
          patchState(store, { state: { status: 'signedOut' } });
          return;
        }
        try {
          await attach();
          const result = await firstValueFrom(store._accounts.orders());
          patchState(store, { state: { status: 'signedIn', orders: result.orders } });
        } catch {
          patchState(store, { state: { status: 'failed' } });
        }
      },
      /** Hands the browser to the issuer. Everything after this happens on the way back. */
      signIn(): void {
        patchState(store, { state: { status: 'signingIn' } });
        store._auth.signIn();
      },
      /** Ends the session at the issuer too, which is what makes it look ended. */
      signOut(): void {
        patchState(store, { state: { status: 'signedOut' } });
        store._auth.signOut();
      },
      imageFailed(sku: string): void {
        patchState(store, (state) => ({ broken: new Set(state.broken).add(sku) }));
      },
    };
  }),
);
