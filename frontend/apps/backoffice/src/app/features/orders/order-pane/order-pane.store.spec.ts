import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { OrderSummary } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { OrdersStore } from '../orders.store';
import { OrderPaneStore } from './order-pane.store';

const Orders = 'http://api/api/orders';

const order = (orderId: string, status: string): OrderSummary =>
  ({
    orderId,
    status,
    recipientName: 'Ana',
    city: 'Madrid',
    total: 50,
    currency: 'EUR',
    createdAt: '2026-09-29T10:00:00Z',
  }) as unknown as OrderSummary;

/** The pane's own state (ADR 0047): the open order's detail, which only the pane reads. */
describe('the order pane store', () => {
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
        OrderPaneStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  const details = (id: string) => http.match((req) => req.url === `${Orders}/${id}`);

  async function open(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const pane = TestBed.inject(OrderPaneStore);
    TestBed.tick();
    http
      .match((req) => req.url === Orders)
      .forEach((req) => req.flush({ orders: [order('a', 'Confirmed')], total: 1, counts: [] }));
    // One turn for the list's answer to land; the detail is still being asked, so the app is not stable yet.
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
    return pane;
  }

  it('asks for no detail while no order is open', async () => {
    await open('/');

    expect(http.match((req) => req.url.startsWith(`${Orders}/`))).toHaveLength(0);
  });

  it('asks for the open order detail in the reader culture', async () => {
    await open('/?order=a');
    const [request] = details('a');

    expect(request.request.params.has('culture')).toBe(true);
  });

  /** The page's store knows nothing of the pane: the pane sees its order change and asks again. */
  it('asks for the detail again when a move changes the open order', async () => {
    await open('/?order=a');
    details('a').forEach((req) => req.flush({ order: { lines: [] } }));
    // The detail has arrived before the move, as it has in a person's hands.
    await new Promise((resolve) => setTimeout(resolve));

    TestBed.inject(OrdersStore).apply(order('a', 'Confirmed'), 'Ship', 'pane');
    http.expectOne(`${Orders}/a/move`).flush(order('a', 'Shipped'));
    // The pane sees the row change and reloads; the resource asks on the next cycle.
    TestBed.tick();
    TestBed.tick();

    expect(details('a')).toHaveLength(1);
  });
});
