import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { OrderSummary } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { OrderStatusLabel } from './order-status-label';

const order: OrderSummary = {
  orderId: '0191d2a4-5c3e-7000-8000-000000000001',
  status: 'PaymentAuthorized',
  mode: 'Live',
  mandate: null,
  total: { amount: 2995, currency: 'EUR' },
  lineCount: 1,
  recipientName: 'Ana Ruiz',
  city: 'Madrid',
  isPaid: true,
  isCaptured: true,
  stopCode: null,
  stopDetail: null,
  oversold: null,
  preview: [],
  createdAt: '2026-10-31T11:58:00Z',
};

function render(summary: OrderSummary): HTMLElement {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n()],
  });
  const fixture = TestBed.createComponent(OrderStatusLabel);
  fixture.componentRef.setInput('order', summary);
  fixture.detectChanges();
  for (const translations of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
    translations.flush({});
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

/**
 * A sales channel took the money for stock the shop could not hold (phase 16, S3;
 * ADR 0070): the order is kept, and its status says so where nobody misses it, with
 * the ledger's reason on hover.
 */
describe('an order status label', () => {
  it('says an order a channel oversold, with the reason', () => {
    const tag = render({ ...order, oversold: 'TAZ-001-BLU: 2 asked for, 1 available.' }).querySelector('.tag--danger');

    expect(tag?.getAttribute('title')).toBe('TAZ-001-BLU: 2 asked for, 1 available.');
  });

  it('says nothing of the kind for any other order', () => {
    expect(render(order).querySelector('.tag--danger')).toBeNull();
  });
});
