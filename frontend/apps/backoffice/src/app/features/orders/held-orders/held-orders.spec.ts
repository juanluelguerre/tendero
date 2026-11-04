import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { HeldOrder } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { HeldOrders } from './held-orders';

/** Only the words these tests read, as the English file says them. */
const words = {
  orders: {
    held: {
      title: '{{count}} paid orders could not be received',
      reason: {
        NotInCatalogue: 'It sells {{about}}, which the catalogue does not have.',
        NoAddress: 'It has nowhere to be sent.',
      },
    },
  },
};

const unsold: HeldOrder = {
  channel: 'shopify',
  externalId: '8720535781562',
  number: '#1013',
  reason: {
    code: 'NotInCatalogue',
    about: 'B13BAG0711-OLIVE',
    text: 'Order #1013 sells B13BAG0711-OLIVE, which the catalogue does not.',
  },
  page: 'https://tendero-dev.myshopify.com/admin/orders/8720535781562',
  heldSince: '2026-11-03T17:21:53Z',
  lastTriedAt: '2026-11-04T09:00:00Z',
};

/**
 * The orders a channel took and charged for that could not be received (P16-29): a
 * band above the orders, only when there are any, so the person who ships knows an
 * order is paid and waiting, why, and where to mend it.
 */
describe('the held orders', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function open(orders: HeldOrder[]) {
    TestBed.configureTestingModule({
      imports: [HeldOrders],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    const fixture = TestBed.createComponent(HeldOrders);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    // The band asks for its words only once there is something to say: the orders first.
    http.expectOne('http://api/api/channels/held-orders').flush({ orders });
    await fixture.whenStable();
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush(words);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows nothing when no order is held', async () => {
    const element = await open([]);

    expect(element.querySelector('[data-held-orders]')).toBeNull();
  });

  it('says each held order, why, and links to it in the channel', async () => {
    const element = await open([
      unsold,
      { ...unsold, externalId: '2', number: '#1014', page: null, reason: { code: 'NoAddress', about: null, text: '' } },
    ]);

    const band = element.querySelector('[data-held-orders]');
    expect(band?.textContent).toContain('2');
    const rows = [...element.querySelectorAll('[data-held-order]')];
    expect(rows.map((row) => row.querySelector('[data-number]')?.textContent?.trim())).toEqual(['#1013', '#1014']);
    expect(rows[0].textContent).toContain('B13BAG0711-OLIVE');
    expect(rows[0].querySelector('a')?.getAttribute('href')).toBe(unsold.page);
    expect(rows[1].querySelector('a')).toBeNull();
  });
});
