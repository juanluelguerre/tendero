import { computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { form, maxLength } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { AddressEntryView, AddressRequest, Money, ShippingOptionView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { AddressBookStore } from '../../addresses/address-book.store';
import { addressOf, addressSchema, type AddressDraft } from '../../addresses/address-draft';
import { CartStore } from '../../cart/cart.store';
import { OrderService } from '../../data-access/order.service';
import { ShopLinks } from '../../shop-links';
import { checkoutProblem } from './checkout-problem';

/**
 * The test cards the fake provider understands.
 *
 * They are on the screen on purpose. The provider is a fake and the demo is the
 * deliverable, so the four outcomes a shop has to handle — paid, declined,
 * provider down, and authorised-then-failed — need to be one click apart. A real
 * adapter replaces this block with a card field and nothing else changes.
 */
export const CheckoutCards = ['card-ok', 'card-declined', 'card-unreachable', 'card-capture-fails'] as const;

/** Where the order goes when it is an address typed here rather than one kept in the account. */
export const NewAddress = 'new';

/** The book's ceiling on a label, the server's `AddressBook.MaxLabelLength`. */
const MaxLabelLength = 40;

interface CheckoutState {
  /**
   * Where it goes (P15-3): an entry of the signed-in shopper's address book by its
   * id, {@link NewAddress} for the form, or null while a book with no default
   * waits for the shopper to choose — nothing is chosen for them.
   */
  readonly where: string | null;
  readonly options: readonly ShippingOptionView[];
  readonly chosen: ShippingOptionView | null;
  readonly instrument: string;
  readonly rating: boolean;
  readonly paying: boolean;
  readonly problem: string | null;
}

/**
 * The checkout's own state (ADR 0047): the address, the delivery options it is
 * priced for, the card, and what the till answered. The basket and its quote
 * are the shop's one cart.
 *
 * The interesting state is the **409**. When the price moved between the quote
 * and the till, the server answers with the new fingerprint and the new total,
 * and the page says what changed rather than just refusing. That path is what
 * a quote's fingerprint exists for (ADR 0016), and it is the same mechanism an
 * AP2 mandate check uses.
 */
export const CheckoutStore = signalStore(
  withState<CheckoutState>(() => ({
    where: NewAddress,
    options: [],
    chosen: null,
    instrument: CheckoutCards[0],
    rating: false,
    paying: false,
    problem: null,
  })),
  // The address is a Signal Form (ADR 0051): its model a writable signal in the
  // store's props — a form writes its model, which the store's frozen state is
  // not for — and its inputs bound to `addressForm` with `[formField]`. What the
  // server needs before it can price delivery is the schema's, so "is the
  // address complete?" is the form's validity, not a check written by hand.
  withProps(() => {
    /*
     * The address is prefilled with somewhere the shop actually delivers,
     * because this is a demo catalogue and making a reviewer type an address to
     * see a checkout is a way to lose them. A real shop starts empty, or from
     * the account's book.
     *
     * **The name is not.** It used to be "Ana Ruiz" — the seeded shopper's name
     * — for every guest, so the backoffice listed every guest order as hers
     * while her account showed one (UI review, 2026-09-11). It is the signed-in
     * person's name, or whatever a guest types.
     */
    const draft = signal<AddressDraft>({
      recipientName: inject(AuthStore).identity()?.name ?? '',
      line1: 'Calle Mayor 1',
      line2: '',
      city: 'Madrid',
      region: '',
      postalCode: '28013',
      countryCode: 'ES',
      phone: '',
    });
    // Named, so each input's `name` is `address.<field>`: stable for autofill and for the browser suites.
    // The account's address book edits the same draft under the same schema (address-draft.ts).
    const addressForm = form(draft, addressSchema, { name: 'address' });

    // *Keep it in my addresses, as…* (P15-3): off by default, and its label the
    // shopper's words. It is sent after the order, never with it.
    const keepDraft = signal({ keep: false, label: '' });
    const keepForm = form(keepDraft, (keep) => maxLength(keep.label, MaxLabelLength), { name: 'keep' });

    return { _draft: draft, addressForm, _keepDraft: keepDraft, keepForm };
  }),
  withProps(() => ({
    _signedIn: inject(AuthStore).isSignedIn,
    _book: inject(AddressBookStore),
    _cart: inject(CartStore),
    _orders: inject(OrderService),
    _router: inject(Router),
    _links: inject(ShopLinks),
    _culture: inject(CultureStore),
    _transloco: inject(TranslocoService),
    /**
     * One key per attempt at THIS basket, minted once. Pressing pay twice, a
     * proxy retrying a timeout and an agent reissuing the request are all the
     * same key, and all three must be one order.
     */
    _idempotencyKey: crypto.randomUUID(),
  })),
  withComputed(({ _draft, _keepDraft, _book, _signedIn, where }) => {
    /** The kept entries checkout offers: a signed-in shopper's book, nothing for a guest. */
    const kept = computed<readonly AddressEntryView[]>(() => (_signedIn() ? _book.entries() : []));
    const entry = computed(() => kept().find((each) => each.id === where()) ?? null);

    return {
      kept,
      /** The address as the API takes it — the entry's, or the form's with the optional fields null when empty; null until one is chosen. */
      address: computed<AddressRequest | null>(() =>
        where() === NewAddress ? addressOf(_draft()) : (entry()?.address ?? null),
      ),
      /**
       * Where the invoice goes, said on the page and sent with the order: the
       * billing default, when it is another entry than the one it is sent to.
       * Otherwise none is sent, and the order bills to where it is delivered, as
       * it always has.
       */
      billing: computed<AddressEntryView | null>(() => {
        const billing = _signedIn() ? _book.billingDefault() : null;
        return billing && billing.id !== where() ? billing : null;
      }),
      /** The box is offered to a signed-in shopper typing a new address, with room left in the book. */
      canKeep: computed(() => _signedIn() && where() === NewAddress && !_book.full()),
      /** A box ticked needs a label: what the shopper will find it by. */
      keepValid: computed(() => {
        const { keep, label } = _keepDraft();
        return !keep || (label.trim().length > 0 && label.trim().length <= MaxLabelLength);
      }),
      /** A new address with no room for it in the book: no box, and a line that says why. */
      bookFull: computed(() => _signedIn() && where() === NewAddress && _book.full()),
    };
  }),
  withMethods((store) => {
    const money = (amount: Money): string => formatPrice(amount, store._culture.active());

    /** One sentence a shopper can act on — see `checkoutProblem`. */
    const describe = (error: unknown): string =>
      checkoutProblem(
        error as { status?: number; error?: unknown },
        (key, params) => store._transloco.translate(key, params),
        money,
        // An article is called what the basket calls it.
        (sku) => store._cart.cart()?.lines.find((line) => line.sku === sku)?.productName ?? null,
      );

    /**
     * Choosing re-quotes, because `FreeShipping` is a promotion effect and the
     * total the shopper agrees to includes delivery. The fingerprint moves with
     * it, which is exactly what makes the mismatch check meaningful.
     */
    const choose = async (option: ShippingOptionView): Promise<void> => {
      patchState(store, { chosen: option });
      await store._cart.quoteWithShipping(option.amount);
    };

    /** The typed address kept in the book under the shopper's label; false when it could not be, never a thrown error. */
    const keepAddress = async (address: AddressRequest): Promise<boolean> => {
      try {
        return await store._book.add({
          label: store._keepDraft().label.trim(),
          address,
          deliveryDefault: false,
          billingDefault: false,
        });
      } catch {
        return false;
      }
    };

    const loadOptions = async (): Promise<void> => {
      // The whole address, recipient included: the server validates it as one,
      // and asking with a blank name answered 400 — which this page reads as
      // "we do not deliver there" (found live, 2026-09-11, the day the name
      // stopped being prefilled for guests).
      if (store.where() === NewAddress && !store.addressForm().valid()) return;
      const address = store.address();
      // A book with no default asks first: nothing is priced for an address nobody chose.
      if (!address) {
        patchState(store, { options: [], chosen: null });
        return;
      }

      patchState(store, { rating: true, problem: null });
      try {
        const response = await store._cart.shippingOptions(address);
        patchState(store, { options: response.options });
        // Preselect the cheapest, which the port guarantees is first. A checkout
        // with nothing chosen is a checkout with no total.
        if (response.options.length > 0) await choose(response.options[0]);
      } catch (error) {
        // "We do not deliver there yet" is a business fact, and the one refusal
        // on this page worth saying in the shopper's own language rather than
        // relaying the server's English sentence.
        patchState(store, {
          options: [],
          chosen: null,
          problem:
            (error as { status?: number }).status === 400
              ? store._transloco.translate('checkout.unserved', { country: address.countryCode })
              : describe(error),
        });
      } finally {
        patchState(store, { rating: false });
      }
    };

    return {
      money,
      choose,
      loadOptions,
      /**
       * The basket, and — for a signed-in shopper — the address book (P15-3): the
       * delivery default preselected and shown, never a form to retype. A book
       * with entries and no default asks; an empty book, or a guest, opens on the
       * form, as checkout always has.
       */
      async start(): Promise<void> {
        await store._cart.load();
        if (store._signedIn()) {
          await store._book.load();
          const preselected = store._book.deliveryDefault()?.id;
          patchState(store, { where: preselected ?? (store.kept().length > 0 ? null : NewAddress) });
        }
        if (!store._cart.isEmpty()) await loadOptions();
      },
      /** Another entry of the book, or {@link NewAddress}: delivery is priced again for it. */
      sendTo(where: string): Promise<void> {
        patchState(store, { where });
        return loadOptions();
      },
      /**
       * Any field of the address, once it has changed: the options and the total
       * follow it (the redesign's R-5.2, S-F3). There used to be a button, *¿Cómo
       * quieres recibirlo?*, that had to be pressed before a total existed, and
       * Pagar stayed disabled until somebody found it; the shipping depends on
       * the address, so the address is what asks.
       */
      addressChanged(): Promise<void> {
        return loadOptions();
      },
      chooseInstrument(instrument: string): void {
        patchState(store, { instrument });
      },
      async pay(): Promise<void> {
        const option = store.chosen();
        const quote = store._cart.quote();
        const address = store.address();
        if (!option || !quote || !address || !store.keepValid()) return;

        // Decided before the order: placing it changes nothing about the book, but
        // what the shopper ticked is what is kept.
        const keep = store.canKeep() && store._keepDraft().keep;
        const billing = store.billing();

        patchState(store, { paying: true, problem: null });
        try {
          const order = await store._orders.place(
            {
              shippingAddress: address,
              ...(billing ? { billingAddress: billing.address } : {}),
              shippingOptionCode: option.code,
              quoteHash: quote.inputHash,
              instrument: store.instrument(),
            },
            store._idempotencyKey,
            store._cart.token(),
            store._culture.active(),
          );

          // The basket became an order. Clearing the token is what stops the next
          // page load asking for a cart that is gone.
          store._cart.clear();

          // Kept by a second call after the order, so a full book or a failed save
          // never stops an order; the order page says when it was not kept.
          const kept = keep ? await keepAddress(address) : undefined;

          await store._router.navigate(store._links.order(order.orderId), {
            state: kept === undefined ? { order } : { order, addressKept: kept },
          });
        } catch (error) {
          patchState(store, { problem: describe(error) });
          // The price moved. Re-quoting here means the summary and the button show
          // the NEW total immediately, so the second press agrees to what is on
          // screen rather than to what was.
          if ((error as { status?: number })?.status === 409) await choose(option);
        } finally {
          patchState(store, { paying: false });
        }
      },
    };
  }),
);
