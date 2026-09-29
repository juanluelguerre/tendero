import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { OrderSummary } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { OrdersStore } from './orders.store';

const Orders = 'http://api/api/orders';

const order = (orderId: string, status: string): OrderSummary =>
  ({ orderId, status, recipientName: 'Ana', city: 'Madrid', total: 50, currency: 'EUR', createdAt: '2026-09-29T10:00:00Z' }) as unknown as OrderSummary;

/** The orders page's state without its screen (ADR 0047): the URL in, the list and the moves out. */
describe('the orders store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        OrdersStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(OrdersStore);
    TestBed.tick();
    return store;
  }

  const listed = () => http.match((req) => req.url === Orders);

  /** The job this screen is for is shipping (R-0, B3): no status in the URL is the orders to ship. */
  it('opens on the orders to ship, first page', async () => {
    const store = await at('/');
    const [request] = listed();

    expect(store.statusKey()).toBe('Confirmed');
    expect([request.request.params.get('status'), request.request.params.get('page')]).toEqual(['Confirmed', '1']);
  });

  /** A status typed by hand that the shop does not have lists everything, rather than asking for a 400. */
  it('lists every order for a status the shop does not have', async () => {
    await at('/?status=Nope');

    expect(listed()[0].request.params.has('status')).toBe(false);
  });

  it('replaces the row a move comes back with, and keeps the server sentence when it refuses', async () => {
    const store = await at('/');
    listed()[0].flush({ orders: [order('a', 'Confirmed'), order('b', 'Confirmed')], total: 2, counts: [] });
    await TestBed.inject(ApplicationRef).whenStable();

    store.apply(order('a', 'Confirmed'), 'Ship', 'row');
    http.expectOne(`${Orders}/a/move`).flush(order('a', 'Shipped'));
    store.apply(order('b', 'Confirmed'), 'Ship', 'row');
    http.expectOne(`${Orders}/b/move`).flush('Cannot ship an order that is not paid.', { status: 409, statusText: 'Conflict' });

    expect(store.orders().map((row) => row.status)).toEqual(['Shipped', 'Confirmed']);
    expect(store.refusal()).toEqual({ b: 'Cannot ship an order that is not paid.' });
    expect(store.busy()).toBeNull();
  });
});
