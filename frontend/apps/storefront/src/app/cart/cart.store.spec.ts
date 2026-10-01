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
      http.expectOne('http://api/api/pricing/quote?culture=es').flush({ currency: 'EUR', lines: [] });
    await adding;
  }

  it('keeps the token the answer carries, and quotes the lines after every change', async () => {
    const store = TestBed.inject(CartStore);
    await added(store, cart([{ sku: 'SKU-1', quantity: 2 }]));

    expect([store.token(), localStorage.getItem('tendero.cart'), store.itemCount(), store.quote()?.currency]).toEqual([
      'T-1',
      'T-1',
      2,
      'EUR',
    ]);
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
      .flush('Out of stock', { status: 409, statusText: 'Conflict' });
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

  it('starts from the token this browser kept', () => {
    localStorage.setItem('tendero.cart', 'KEPT');

    expect(TestBed.inject(CartStore).token()).toBe('KEPT');
  });
});
