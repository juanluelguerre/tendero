import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { AddressBookView, AddressEntryRequest, AddressEntryView, AddressUse } from '@tendero/shared-api';
import { withAccountSection } from '../account-section/with-account-section';
import { AddressBookService } from '../data-access/address-book.service';

/**
 * The signed-in shopper's address book (P15-2, docs/specs/shopper-account.md;
 * ADR 0047): shop-wide, because the account's pages and checkout (P15-3) read the
 * same book.
 *
 * **The server's book is the only one** (`withAccountSection`): every change
 * answers the whole book and it replaces what is held, so the rules — one default
 * of each kind, ten at most, a default removed leaves none — are the server's and
 * are never written twice.
 */
export const AddressBookStore = signalStore(
  { providedIn: 'root' },
  withAccountSection<AddressBookView>('addresses.failed'),
  withProps(() => ({ _addresses: inject(AddressBookService) })),
  withComputed(({ view }) => {
    const entries = computed<readonly AddressEntryView[]>(() => view()?.entries ?? []);
    const max = computed(() => view()?.maxEntries ?? 10);
    return {
      entries,
      max,
      full: computed(() => entries().length >= max()),
      deliveryDefault: computed(() => entries().find((entry) => entry.deliveryDefault) ?? null),
      billingDefault: computed(() => entries().find((entry) => entry.billingDefault) ?? null),
    };
  }),
  withMethods((store) => ({
    load: () => store._load(() => store._addresses.get()),
    find(id: string): AddressEntryView | null {
      return store.entries().find((entry) => entry.id === id) ?? null;
    },
    add: (entry: AddressEntryRequest) => store._change(() => store._addresses.add(entry)),
    edit: (id: string, entry: AddressEntryRequest) => store._change(() => store._addresses.edit(id, entry)),
    remove: (id: string) => store._change(() => store._addresses.remove(id)),
    useFor: (id: string, use: AddressUse) => store._change(() => store._addresses.useFor(id, use)),
  })),
);
