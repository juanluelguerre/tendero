import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { TranslocoService } from '@jsverse/transloco';
import type { AddressBookView, AddressEntryRequest, AddressEntryView, AddressUse } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { refusalSentence } from '@tendero/shared-util';
import { firstValueFrom, type Observable } from 'rxjs';
import { AddressBookService } from '../data-access/address-book.service';
import { CustomerLink } from '../data-access/customer-link';

interface AddressBookState {
  /** The book as the server last answered it; null until it has. */
  readonly book: AddressBookView | null;
  readonly status: 'idle' | 'loading' | 'ready' | 'failed';
  /** A change on its way: one at a time, so a second press waits for the first answer. */
  readonly saving: boolean;
  /** What the server said no with, in the shopper's language; null when nothing failed. */
  readonly problem: string | null;
}

/**
 * The signed-in shopper's address book (P15-2, docs/specs/shopper-account.md;
 * ADR 0047): shop-wide, because the account's pages and checkout (P15-3) read the
 * same book.
 *
 * **The server's book is the only one.** Every change answers the whole book and
 * it replaces what is held, so the rules — one default of each kind, ten at most,
 * a default removed leaves none — are the server's and are never written twice.
 * A refusal is said in the server's own sentence, in the shopper's language.
 */
export const AddressBookStore = signalStore(
  { providedIn: 'root' },
  withState<AddressBookState>({ book: null, status: 'idle', saving: false, problem: null }),
  withProps(() => ({
    _signedIn: inject(AuthStore).isSignedIn,
    _addresses: inject(AddressBookService),
    _customer: inject(CustomerLink),
    _transloco: inject(TranslocoService),
  })),
  withComputed(({ book }) => {
    const entries = computed<readonly AddressEntryView[]>(() => book()?.entries ?? []);
    const max = computed(() => book()?.maxEntries ?? 10);
    return {
      entries,
      max,
      full: computed(() => entries().length >= max()),
      deliveryDefault: computed(() => entries().find((entry) => entry.deliveryDefault) ?? null),
      billingDefault: computed(() => entries().find((entry) => entry.billingDefault) ?? null),
    };
  }),
  withMethods((store) => {
    /** One change, sent once: the answer replaces the book, a refusal is kept to be said. True when it was applied. */
    const change = async (request: () => Observable<AddressBookView>): Promise<boolean> => {
      if (store.saving()) return false;
      patchState(store, { saving: true, problem: null });
      try {
        patchState(store, { book: await firstValueFrom(request()), status: 'ready', saving: false });
        return true;
      } catch (failure) {
        const said = refusalSentence((failure as { error?: unknown }).error);
        patchState(store, { saving: false, problem: said || store._transloco.translate('addresses.failed') });
        return false;
      }
    };

    return {
      /** The book read, once the customer is linked; nothing at all for a guest. */
      async load(): Promise<void> {
        if (!store._signedIn()) return;
        patchState(store, { status: 'loading' });
        try {
          await store._customer.ensure();
          patchState(store, { book: await firstValueFrom(store._addresses.get()), status: 'ready' });
        } catch {
          patchState(store, { status: 'failed' });
        }
      },
      find(id: string): AddressEntryView | null {
        return store.entries().find((entry) => entry.id === id) ?? null;
      },
      add: (entry: AddressEntryRequest) => change(() => store._addresses.add(entry)),
      edit: (id: string, entry: AddressEntryRequest) => change(() => store._addresses.edit(id, entry)),
      remove: (id: string) => change(() => store._addresses.remove(id)),
      useFor: (id: string, use: AddressUse) => change(() => store._addresses.useFor(id, use)),
      dismiss(): void {
        patchState(store, { problem: null });
      },
    };
  }),
);
