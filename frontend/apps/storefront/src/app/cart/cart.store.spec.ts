import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CartView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CartStore } from './cart.store';

const cart = (lines: { sku: string; quantity: number }[]): CartView =>
  ({ itemCount: lines.reduce((sum, line) => sum + line.quantity, 0), lines }) as unknown as CartView;

/** The basket and what it costs: every change is followed by a fresh quote, and the token travels by hand (ADR 0016, ADR 0047). */
describe('the cart store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: CultureStore, useValue: { active: signal('es') } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Answers the add, then the quote that has to follow it. */
  async function added(store: InstanceType<typeof CartStore>, answer: CartView, token = 'T-1'): Promise<void> {
    const adding = store.add('SKU-1', 2);
    http.expectOne('http://api/api/cart/lines?culture=es').flush(answer, { headers: { 'X-Cart-Token': token } });
    await new Promise((settled) => setTimeout(settled));
    if (answer.lines.length)
      http
        .expectOne('http://api/api/pricing/quote?culture=es')
        .flush({ total: { amount: 0, currency: 'EUR' }, lines: [] });
    await adding;
  }

  it('keeps the token the answer carries, and quotes the lines after every change', async () => {
    const store = TestBed.inject(CartStore);
    await added(store, cart([{ sku: 'SKU-1', quantity: 2 }]));

    expect([
      store.token(),
      localStorage.getItem('tendero.cart'),
      store.itemCount(),
      store.quote()?.total.currency,
    ]).toEqual(['T-1', 'T-1', 2, 'EUR']);
  });

  /** The quote endpoint refuses a basket with no lines, and asking anyway would paint an error over it. */
  it('does not quote an empty basket', async () => {
    const store = TestBed.inject(CartStore);
    await added(store, cart([]));

    expect([store.isEmpty(), store.quote()]).toEqual([true, null]);
  });

  it('keeps the server’s refusal to say beside the button, rather than throwing it', async () => {
    const store = TestBed.inject(CartStore);
    const adding = store.add('SKU-1');
    http
      .expectOne('http://api/api/cart/lines?culture=es')
      .flush({ code: 'Refused', status: 409, title: 'Out of stock' }, { status: 409, statusText: 'Conflict' });
    await adding;

    expect([store.failed(), store.busy()]).toEqual(['Out of stock', false]);
  });

  it('forgets the basket and its token once it became an order', async () => {
    const store = TestBed.inject(CartStore);
    await added(store, cart([{ sku: 'SKU-1', quantity: 1 }]));
    store.clear();

    expect([store.token(), localStorage.getItem('tendero.cart'), store.cart(), store.quote()]).toEqual([
      null,
      null,
      null,
      null,
    ]);
  });

  /**
   * Two changes asked at once — a click on the product page and the browser
   * agent's tool, or a double click — reach the server one after the other, so
   * a late answer can never overwrite a newer basket: the second goes out once
   * the first has answered, and been priced.
   */
  it('makes one change at a time, in the order they were asked', async () => {
    const store = TestBed.inject(CartStore);
    const first = store.add('SKU-1');
    const second = store.add('SKU-2');
    await new Promise((settled) => setTimeout(settled));
    const inFlight = http.match('http://api/api/cart/lines?culture=es');
    inFlight[0]?.flush(cart([{ sku: 'SKU-1', quantity: 1 }]), { headers: { 'X-Cart-Token': 'T-1' } });
    await new Promise((settled) => setTimeout(settled));
    http
      .expectOne('http://api/api/pricing/quote?culture=es')
      .flush({ total: { amount: 0, currency: 'EUR' }, lines: [] });
    await first;
    const next = http.match('http://api/api/cart/lines?culture=es');
    next[0]?.flush(
      cart([
        { sku: 'SKU-1', quantity: 1 },
        { sku: 'SKU-2', quantity: 1 },
      ]),
      { headers: { 'X-Cart-Token': 'T-1' } },
    );
    await new Promise((settled) => setTimeout(settled));
    http
      .expectOne('http://api/api/pricing/quote?culture=es')
      .flush({ total: { amount: 0, currency: 'EUR' }, lines: [] });
    await second;

    expect([inFlight.length, next.length, next[0]?.request.body.sku, store.itemCount()]).toEqual([1, 1, 'SKU-2', 2]);
  });

  it('starts from the token this browser kept', () => {
    localStorage.setItem('tendero.cart', 'KEPT');

    expect(TestBed.inject(CartStore).token()).toBe('KEPT');
  });
});
