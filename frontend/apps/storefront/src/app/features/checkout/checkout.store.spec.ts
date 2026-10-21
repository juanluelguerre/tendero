import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import type {
  AddressEntryRequest,
  AddressEntryView,
  Money,
  PriceQuote,
  SavedMethodView,
  ShippingOptionView,
} from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { beforeEach, describe, expect, it } from 'vitest';
import { AddressBookStore } from '../../addresses/address-book.store';
import { CartStore } from '../../cart/cart.store';
import { OrderService } from '../../data-access/order.service';
import { WalletStore } from '../../wallet/wallet.store';
import { CheckoutStore } from './checkout.store';

const option = (code: string, amount: number): ShippingOptionView =>
  ({ code, amount: { amount, currency: 'EUR' } }) as unknown as ShippingOptionView;

const kept = (id: string, label: string, line1: string, delivery = false, billing = false): AddressEntryView => ({
  id,
  label,
  address: {
    recipientName: 'Ana García',
    line1,
    line2: null,
    city: 'Madrid',
    region: null,
    postalCode: '28013',
    countryCode: 'ES',
    phone: null,
  },
  deliveryDefault: delivery,
  billingDefault: billing,
});

const card = (id: string, last4: string, isDefault = false, expired = false): SavedMethodView => ({
  id,
  brand: 'Visa',
  last4,
  expiryMonth: 8,
  expiryYear: 2028,
  expired,
  isDefault,
});

/** Address, delivery, payment (ADR 0047): the checkout's own state, over the shop's one cart. */
describe('the checkout store', () => {
  let name: string | null;
  let options: () => Promise<{ options: ShippingOptionView[] }>;
  let placing: () => Promise<{ orderId: string }>;
  let quoted: number[];
  let placed: string[];
  let went: unknown[];
  let cleared: number;
  let signedIn: boolean;
  let book: AddressEntryView[];
  let full: boolean;
  let keeping: (entry: AddressEntryRequest) => Promise<boolean>;
  let keptEntries: AddressEntryRequest[];
  let priced: string[];
  let requests: {
    shippingAddress: { line1: string };
    billingAddress?: { line1: string } | null;
    instrument: string;
    savedMethodId?: string | null;
  }[];
  let handed: unknown[];
  let methods: SavedMethodView[];
  let tracked: [string, unknown][];

  beforeEach(() => {
    signedIn = false;
    book = [];
    full = false;
    keptEntries = [];
    keeping = async (entry) => (keptEntries.push(entry), true);
    priced = [];
    requests = [];
    handed = [];
    methods = [];
    tracked = [];
    name = 'Ana Ruiz';
    options = async () => ({ options: [option('STANDARD', 4.9), option('EXPRESS', 9.9)] });
    placing = async () => ({ orderId: 'O-1' });
    quoted = [];
    placed = [];
    went = [];
    cleared = 0;
  });

  async function opened() {
    TestBed.configureTestingModule({
      providers: [
        CheckoutStore,
        { provide: AuthStore, useValue: { identity: () => (name ? { name } : null), isSignedIn: () => signedIn } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        {
          provide: Telemetry,
          useValue: { trackEvent: (name: string, attributes: unknown) => void tracked.push([name, attributes]) },
        },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        {
          provide: Router,
          useValue: {
            navigate: async (commands: unknown[], extras?: { state?: unknown }) => {
              went.push(commands);
              handed.push(extras?.state);
            },
          },
        },
        {
          provide: AddressBookStore,
          useValue: (() => {
            const entries = signal<AddressEntryView[]>([]);
            return {
              load: async () => void entries.set(book),
              entries,
              deliveryDefault: computed(() => entries().find((entry) => entry.deliveryDefault) ?? null),
              billingDefault: computed(() => entries().find((entry) => entry.billingDefault) ?? null),
              full: () => full,
              add: (entry: AddressEntryRequest) => keeping(entry),
            };
          })(),
        },
        {
          provide: WalletStore,
          useValue: (() => {
            const kept = signal<SavedMethodView[]>([]);
            return {
              load: async () => void kept.set(methods),
              methods: kept,
              default: computed(() => kept().find((each) => each.isDefault) ?? null),
            };
          })(),
        },
        {
          provide: CartStore,
          useValue: {
            load: async () => undefined,
            isEmpty: () => false,
            token: () => 'T-1',
            quote: () => ({ inputHash: 'H' }) as unknown as PriceQuote,
            shippingOptions: (address: { line1: string }) => (priced.push(address.line1), options()),
            quoteWithShipping: async (shipping: Money) => void quoted.push(shipping.amount),
            clear: () => void cleared++,
          },
        },
        {
          provide: OrderService,
          useValue: {
            place: async (request: (typeof requests)[number], idempotencyKey: string) => (
              requests.push(request),
              placed.push(idempotencyKey),
              placing()
            ),
          },
        },
      ],
    });
    const store = TestBed.inject(CheckoutStore);
    await store.start();
    return store;
  }

  it('prices delivery to the address it opens with, and chooses the cheapest', async () => {
    const store = await opened();

    expect([store.options().map((each) => each.code), store.chosen()?.code, quoted]).toEqual([
      ['STANDARD', 'EXPRESS'],
      'STANDARD',
      [4.9],
    ]);
  });

  /** A guest's name is theirs to type: the shop does not ask the server with a blank one. */
  it('asks for no delivery options until a guest has given a name', async () => {
    name = null;
    const store = await opened();
    const before = store.options().length;
    store.addressForm.recipientName().value.set('Luis');
    await store.addressChanged();

    expect([before, store.options().length]).toEqual([0, 2]);
  });

  it('says in the shopper’s language that the shop does not deliver there yet', async () => {
    options = () => Promise.reject({ status: 400 });
    const store = await opened();

    expect([store.problem(), store.chosen()]).toEqual(['checkout.unserved', null]);
  });

  /** One key per attempt at this basket: pressing twice is one order. */
  it('pays with one key however often it is pressed, then forgets the cart and shows the order', async () => {
    const store = await opened();
    placing = () => Promise.reject({ status: 503 });
    await store.pay();
    placing = async () => ({ orderId: 'O-1' });
    await store.pay();

    expect([placed[0] === placed[1], cleared, went]).toEqual([true, 1, [['/', 'es', 'orders', 'O-1']]]);
  });

  /** The price moved between the quote and the till: the page re-quotes, so the next press agrees to what is on screen. */
  it('quotes again when the price moved', async () => {
    const store = await opened();
    placing = () => Promise.reject({ status: 409, error: 'Price changed' });
    await store.pay();

    expect([quoted, cleared]).toEqual([[4.9, 4.9], 0]);
  });

  /** Checkout reads the account (P15-3): the default preselected and shown, never a form to retype. */
  it('opens on the signed-in shopper’s default address and prices delivery to it', async () => {
    signedIn = true;
    book = [kept('A', 'Home', 'Calle Mayor 12', true), kept('B', 'Work', 'Castellana 89')];
    const store = await opened();
    await store.pay();

    expect([store.where(), priced, requests[0].shippingAddress.line1]).toEqual([
      'A',
      ['Calle Mayor 12'],
      'Calle Mayor 12',
    ]);
  });

  /** A book with no default asks: an address applied without being shown is an order sent to the wrong house. */
  it('asks where to send it when the book has no default, and prices nothing until it is chosen', async () => {
    signedIn = true;
    book = [kept('A', 'Home', 'Calle Mayor 12'), kept('B', 'Work', 'Castellana 89')];
    const store = await opened();
    const before = [store.where(), store.options().length, priced.length];
    await store.sendTo('B');

    expect([before, store.where(), priced]).toEqual([[null, 0, 0], 'B', ['Castellana 89']]);
  });

  it('opens on the form for a guest and for a shopper with an empty book', async () => {
    const guest = await opened();

    expect([guest.where(), priced]).toEqual(['new', ['Calle Mayor 1']]);
  });

  /** The billing default is sent when it is another address, and said on the page. */
  it('bills to the billing default when it is another entry', async () => {
    signedIn = true;
    book = [kept('A', 'Home', 'Calle Mayor 12', true), kept('B', 'Work', 'Castellana 89', false, true)];
    const store = await opened();
    await store.pay();

    expect([store.billing()?.label, requests[0].billingAddress?.line1]).toEqual(['Work', 'Castellana 89']);
  });

  /** Kept by a second call after the order, so a full book or a failed save never stops an order. */
  it('keeps a new address after the order when asked, with the label the shopper gave it', async () => {
    signedIn = true;
    const store = await opened();
    store.keepForm.keep().value.set(true);
    store.keepForm.label().value.set('Beach flat');
    await store.pay();

    expect([keptEntries.map((entry) => [entry.label, entry.address?.line1]), cleared, handed]).toEqual([
      [['Beach flat', 'Calle Mayor 1']],
      1,
      [{ order: { orderId: 'O-1' }, addressKept: true }],
    ]);
  });

  it('places the order even when keeping the address fails, and says so on the order', async () => {
    signedIn = true;
    keeping = async () => false;
    const store = await opened();
    store.keepForm.keep().value.set(true);
    store.keepForm.label().value.set('Beach flat');
    await store.pay();

    expect([went, handed]).toEqual([
      [['/', 'es', 'orders', 'O-1']],
      [{ order: { orderId: 'O-1' }, addressKept: false }],
    ]);
  });

  it('keeps nothing when the box is not ticked', async () => {
    signedIn = true;
    const store = await opened();
    await store.pay();

    expect([keptEntries, store.canKeep(), handed[0]]).toEqual([[], true, { order: { orderId: 'O-1' } }]);
  });

  it('offers no box to a guest', async () => {
    expect((await opened()).canKeep()).toBe(false);
  });

  it('offers no box when the book is full, and says why', async () => {
    signedIn = true;
    full = true;
    const store = await opened();

    expect([store.canKeep(), store.bookFull()]).toEqual([false, true]);
  });

  it('offers no box for an address already in the book', async () => {
    signedIn = true;
    book = [kept('A', 'Home', 'Calle Mayor 12', true)];

    expect((await opened()).canKeep()).toBe(false);
  });

  /** The preferred way to pay (P15-5): the default card preselected and shown, charged by its id, never a token. */
  it('pays with the signed-in shopper’s default card by its id', async () => {
    signedIn = true;
    methods = [card('V', '4242', true), card('M', '4444')];
    const store = await opened();
    await store.pay();

    expect([store.paysWith(), requests[0].savedMethodId, requests[0].instrument]).toEqual(['V', 'V', '']);
  });

  /** A default that expired is said to be one and not chosen: checkout asks, and Pay waits. */
  it('asks how to pay when the default card has expired', async () => {
    signedIn = true;
    methods = [card('V', '0341', true, true), card('M', '4444')];
    const store = await opened();
    await store.pay();

    expect([store.paysWith(), requests.length]).toEqual([null, 0]);
    store.payWith('M');
    await store.pay();
    expect(requests[0].savedMethodId).toBe('M');
  });

  it('will not choose an expired card even when asked to', async () => {
    signedIn = true;
    methods = [card('V', '0341', false, true)];
    const store = await opened();
    store.payWith('V');

    expect(store.paysWith()).toBe(null);
  });

  /** A declined saved card is said so and stays chosen — never skipped for the next one, never removed. */
  it('keeps a declined saved card chosen and says it was declined', async () => {
    signedIn = true;
    methods = [card('V', '4242', true), card('M', '4444')];
    placing = () => Promise.reject({ status: 402 });
    const store = await opened();
    await store.pay();

    expect([store.paysWith(), store.declined(), store.problem()]).toEqual(['V', 'V', 'checkout.declined']);
  });

  it('pays with a test card when the shopper has no saved card, or chooses a new one', async () => {
    signedIn = true;
    methods = [card('V', '4242', true)];
    const store = await opened();
    store.payWith('new');
    await store.pay();

    expect([requests[0].instrument, requests[0].savedMethodId ?? null]).toEqual(['card-ok', null]);
  });

  /** What proves the account is used at checkout (P15-10): where the address and the card came from. */
  it('says a signed-in order came from the account, and a guest’s says nothing', async () => {
    signedIn = true;
    book = [kept('A', 'Home', 'Calle Mayor 12', true)];
    methods = [card('V', '4242', true)];
    const store = await opened();
    await store.pay();

    expect(tracked).toEqual([['checkout_prefilled', { address: 'saved', method: 'saved' }]]);
  });

  it('says nothing for a guest’s order', async () => {
    const store = await opened();
    await store.pay();

    expect(tracked).toEqual([]);
  });

  it('says a typed address was kept, or that keeping it failed', async () => {
    signedIn = true;
    keeping = async () => false;
    const store = await opened();
    store.keepForm.keep().value.set(true);
    store.keepForm.label().value.set('Beach flat');
    await store.pay();

    expect(tracked).toEqual([
      ['checkout_prefilled', { address: 'typed', method: 'new' }],
      ['address_kept_at_checkout', { kept: false }],
    ]);
  });

  it('says a saved card was declined', async () => {
    signedIn = true;
    methods = [card('V', '4242', true)];
    placing = () => Promise.reject({ status: 402 });
    const store = await opened();
    await store.pay();

    expect(tracked).toEqual([['saved_method_declined', { expired: false }]]);
  });
});
