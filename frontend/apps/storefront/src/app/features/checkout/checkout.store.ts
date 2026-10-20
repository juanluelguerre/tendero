import { computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { form } from '@angular/forms/signals';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { AddressRequest, Money, ShippingOptionView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
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

interface CheckoutState {
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
    return { _draft: draft, addressForm };
  }),
  withProps(() => ({
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
  withComputed(({ _cart, _draft }) => ({
    /** The address as the API takes it: the optional fields null when empty. */
    address: computed<AddressRequest>(() => addressOf(_draft())),
  })),
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

    const loadOptions = async (): Promise<void> => {
      // The whole address, recipient included: the server validates it as one,
      // and asking with a blank name answered 400 — which this page reads as
      // "we do not deliver there" (found live, 2026-09-11, the day the name
      // stopped being prefilled for guests).
      if (!store.addressForm().valid()) return;
      const address = store.address();

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
      async start(): Promise<void> {
        await store._cart.load();
        if (!store._cart.isEmpty()) await loadOptions();
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
        if (!option || !quote) return;

        patchState(store, { paying: true, problem: null });
        try {
          const order = await store._orders.place(
            {
              shippingAddress: store.address(),
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
          await store._router.navigate(store._links.order(order.orderId), { state: { order } });
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
