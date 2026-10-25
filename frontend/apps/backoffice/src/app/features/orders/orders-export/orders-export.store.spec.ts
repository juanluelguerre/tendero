import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { OrdersExportStore, lastMonth } from './orders-export.store';

/** Orders for the accountant (phase 16, E-4): a span of days, one request, a file back, and a sentence when it fails. */
describe('the orders export store', () => {
  let http: HttpTestingController;
  let store: InstanceType<typeof OrdersExportStore>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        OrdersExportStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(OrdersExportStore);
  });

  it('offers last month whole, which is what an accountant asks for', () => {
    expect(lastMonth(new Date(2026, 9, 25))).toEqual({ from: '2026-09-01', to: '2026-09-30' });
    expect(lastMonth(new Date(2026, 0, 10))).toEqual({ from: '2025-12-01', to: '2025-12-31' });
  });

  it('asks for the span chosen and hands back the file', async () => {
    store.span.set({ from: '2026-10-01', to: '2026-10-31' });

    const downloading = store.download();
    expect(store.downloading()).toBe(true);

    const request = http.expectOne((req) => req.url === 'http://api/api/exports/orders');
    expect([
      request.request.method,
      request.request.params.get('from'),
      request.request.params.get('to'),
      request.request.params.get('format'),
    ]).toEqual(['POST', '2026-10-01', '2026-10-31', 'csv']);
    request.flush(new Blob(['Pedido;Día\r\n'], { type: 'text/csv' }));

    expect((await downloading)?.size).toBeGreaterThan(0);
    expect([store.downloading(), store.problem()]).toEqual([false, null]);
  });

  it('asks nothing for a span that ends before it starts, and says so', async () => {
    store.span.set({ from: '2026-10-31', to: '2026-10-01' });

    expect(await store.download()).toBeNull();
    http.expectNone((req) => req.url === 'http://api/api/exports/orders');
    expect(store.problem()).not.toBeNull();
  });

  it('says so when the orders could not be exported, and hands back nothing', async () => {
    const downloading = store.download();

    http
      .expectOne((req) => req.url === 'http://api/api/exports/orders')
      .flush(new Blob(), { status: 500, statusText: 'Server Error' });

    expect(await downloading).toBeNull();
    expect(store.problem()).not.toBeNull();
  });
});
