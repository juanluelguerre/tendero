import { inject } from '@angular/core';
import { patchState, signalStoreFeature, withMethods, withProps, withState } from '@ngrx/signals';
import { TranslocoService } from '@jsverse/transloco';
import { AuthStore } from '@tendero/shared-auth';
import { refusalSentence } from '@tendero/shared-util';
import { firstValueFrom, type Observable } from 'rxjs';
import { CustomerLink } from '../data-access/customer-link';

/** One section of the shopper's account as the server last answered it. */
export interface AccountSectionState<TView> {
  /** The server's answer; null until it has given one. */
  readonly view: TView | null;
  readonly status: 'idle' | 'loading' | 'ready' | 'failed';
  /** A change on its way: one at a time, so a second press waits for the first answer. */
  readonly saving: boolean;
  /** What the server said no with, in the shopper's language; null when nothing failed. */
  readonly problem: string | null;
}

/**
 * What every section of the shopper's account shares (P15-2, P15-4; ADR 0047):
 * the address book and the wallet are read once the customer is linked, and
 * **every change answers the whole section and replaces it**, so the rules are
 * the server's alone and never written twice. A refusal is said in the server's
 * own sentence; `failed` is the section's own sentence for when the server gave
 * none a shopper can read.
 */
export function withAccountSection<TView>(failed: string) {
  return signalStoreFeature(
    withState<AccountSectionState<TView>>({ view: null, status: 'idle', saving: false, problem: null }),
    withProps(() => ({
      _signedIn: inject(AuthStore).isSignedIn,
      _customer: inject(CustomerLink),
      _transloco: inject(TranslocoService),
    })),
    withMethods((store) => ({
      /** The section read once the customer is linked; nothing at all for a guest. */
      async _load(read: () => Observable<TView>): Promise<void> {
        if (!store._signedIn()) return;
        patchState(store, { status: 'loading' });
        try {
          await store._customer.ensure();
          patchState(store, { view: await firstValueFrom(read()), status: 'ready' });
        } catch {
          patchState(store, { status: 'failed' });
        }
      },

      /** One change, sent once: the answer replaces the section, a refusal is kept to be said. True when it was applied. */
      async _change(request: () => Observable<TView>): Promise<boolean> {
        if (store.saving()) return false;
        patchState(store, { saving: true, problem: null });
        try {
          patchState(store, { view: await firstValueFrom(request()), status: 'ready', saving: false });
          return true;
        } catch (failure) {
          const said = refusalSentence((failure as { error?: unknown }).error);
          patchState(store, { saving: false, problem: said || store._transloco.translate(failed) });
          return false;
        }
      },

      dismiss(): void {
        patchState(store, { problem: null });
      },
    })),
  );
}
