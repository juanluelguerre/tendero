import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import type { PriceQuote, ShippingOptionView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from '../../cart/cart.store';
import { OrderService } from '../../data-access/order.service';
import { CheckoutStore } from './checkout.store';

const option = (code: string, amount: number): ShippingOptionView =>
  ({ code, amount }) as unknown as ShippingOptionView;

/** Address, delivery, payment (ADR 0047): the checkout's own state, over the shop's one cart. */
describe('the checkout store', () => {
  let name: string | null;
  let options: () => Promise<{ options: ShippingOptionView[] }>;
  let placing: () => Promise<{ orderId: string }>;
  let quoted: number[];
  let placed: { idempotencyKey: string }[];
  let went: unknown[];
  let cleared: number;

  beforeEach(() => {
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
        { provide: AuthStore, useValue: { identity: () => (name ? { name } : null) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        { provide: Router, useValue: { navigate: async (commands: unknown[]) => void went.push(commands) } },
        {
          provide: CartStore,
          useValue: {
            load: async () => undefined,
            isEmpty: () => false,
            token: () => 'T-1',
            quote: () => ({ inputHash: 'H', currency: 'EUR' }) as unknown as PriceQuote,
            shippingOptions: () => options(),
            quoteWithShipping: async (amount: number) => void quoted.push(amount),
            clear: () => void cleared++,
          },
        },
        {
          provide: OrderService,
          useValue: { place: async (request: { idempotencyKey: string }) => (placed.push(request), placing()) },
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
    store.edit({ recipientName: 'Luis' });
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

    expect([placed[0].idempotencyKey === placed[1].idempotencyKey, cleared, went]).toEqual([
      true,
      1,
      [['/', 'es', 'orders', 'O-1']],
    ]);
  });

  /** The price moved between the quote and the till: the page re-quotes, so the next press agrees to what is on screen. */
  it('quotes again when the price moved', async () => {
    const store = await opened();
    placing = () => Promise.reject({ status: 409, error: 'Price changed' });
    await store.pay();

    expect([quoted, cleared]).toEqual([[4.9, 4.9], 0]);
  });
});
