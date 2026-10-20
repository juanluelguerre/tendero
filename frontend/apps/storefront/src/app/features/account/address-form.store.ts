import { computed, inject, signal } from '@angular/core';
import { form, maxLength, pattern, required } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { AddressEntryRequest, AddressEntryView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { AddressBookStore } from '../../addresses/address-book.store';
import { addressOf, addressSchema, draftOf, emptyAddress, type AddressDraft } from '../../addresses/address-draft';
import { ShopLinks } from '../../shop-links';

/** The label and the two defaults: the entry's own half, beside the address. */
interface EntryDraft {
  label: string;
  deliveryDefault: boolean;
  billingDefault: boolean;
}

/** The book's ceiling on a label, the server's `AddressBook.MaxLabelLength`. */
export const MaxLabelLength = 40;

/**
 * One entry of the address book, added or edited (P15-2; board 03 of the
 * account's mock-ups): the page's own store (ADR 0047).
 *
 * Two Signal Forms (ADR 0051), because the address is the same form as
 * checkout's — the same draft, the same schema, the same names (`address.line1`…)
 * so autofill fills both — and the label and the defaults are the book's alone.
 * Saving goes through the shop-wide book, whose answer is the only book.
 */
export const AddressFormStore = signalStore(
  withProps(() => {
    const id = inject(ActivatedRoute).snapshot.paramMap.get('id');
    const name = inject(AuthStore).identity()?.name ?? '';
    const addressDraft = signal<AddressDraft>(emptyAddress(name));
    const entryDraft = signal<EntryDraft>({ label: '', deliveryDefault: false, billingDefault: false });

    return {
      _id: id,
      _addressDraft: addressDraft,
      _entryDraft: entryDraft,
      addressForm: form(addressDraft, addressSchema, { name: 'address' }),
      entryForm: form(
        entryDraft,
        (entry) => {
          required(entry.label);
          pattern(entry.label, /\S/);
          maxLength(entry.label, MaxLabelLength);
        },
        { name: 'entry' },
      ),
      book: inject(AddressBookStore),
      _router: inject(Router),
      _links: inject(ShopLinks),
    };
  }),
  withComputed((store) => ({
    /** Editing an entry, rather than adding one. */
    editing: computed(() => store._id !== null),
    /** The entry being edited, once the book has answered; null when adding, or when there is no such entry. */
    entry: computed<AddressEntryView | null>(() => (store._id ? store.book.find(store._id) : null)),
    valid: computed(() => store.addressForm().valid() && store.entryForm().valid()),
  })),
  withMethods((store) => ({
    /** The book read, and the entry being edited copied into the forms. */
    async start(): Promise<void> {
      if (store.book.status() !== 'ready') await store.book.load();
      const entry = store.entry();
      if (!entry) return;

      store._addressDraft.set(draftOf(entry.address));
      store._entryDraft.set({
        label: entry.label,
        deliveryDefault: entry.deliveryDefault,
        billingDefault: entry.billingDefault,
      });
    },

    /** Saved and back to the book; a refusal stays on this page, said by the book. */
    async save(): Promise<void> {
      if (!store.valid()) return;

      const entry = store._entryDraft();
      const request: AddressEntryRequest = {
        label: entry.label.trim(),
        address: addressOf(store._addressDraft()),
        deliveryDefault: entry.deliveryDefault,
        billingDefault: entry.billingDefault,
      };

      const saved = store._id ? await store.book.edit(store._id, request) : await store.book.add(request);
      if (saved) await store._router.navigate(store._links.addresses());
    },
  })),
);
