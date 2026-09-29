import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { WaitingStore } from './waiting.store';

const Api = 'http://api/api';

/** How much work waits on each screen, for the rail's badges (ADR 0047: shared state, a root store). */
describe('the waiting store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  function answer(fail: string[] = []) {
    const bodies: Record<string, object> = {
      '/catalog/products': { total: 4 },
      '/knowledge/claims': { total: 7 },
      '/dashboard/operations': { toShip: 3, openReturns: 2 },
      '/inventory/stock': { counts: [{ filter: 'Restock', count: 5 }] },
      '/dashboard/agent-denials': { total: 1 },
    };
    for (const request of http.match((req) => req.url.startsWith(Api) && !req.url.includes('/i18n/'))) {
      const path = Object.keys(bodies).find((key) => request.request.url.startsWith(Api + key));
      if (path && fail.includes(path)) request.flush('down', { status: 503, statusText: 'Unavailable' });
      else if (path) request.flush(bodies[path]);
    }
  }

  const settle = async () => {
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  };

  it('says each count, and nothing before they arrive', async () => {
    const store = TestBed.inject(WaitingStore);
    TestBed.tick();
    expect(store.of()('drafts')).toBeNull();

    answer();
    await settle();

    expect((['drafts', 'claims', 'toShip', 'openReturns', 'lowStock', 'refusals'] as const).map((kind) => store.of()(kind))).toEqual([4, 7, 3, 2, 5, 1]);
  });

  /** A badge that cannot load is no badge, never an error in the navigation. */
  it('lets one count fail alone', async () => {
    const store = TestBed.inject(WaitingStore);
    TestBed.tick();
    answer(['/knowledge/claims']);
    await settle();

    expect([store.of()('claims'), store.of()('drafts')]).toEqual([null, 4]);
  });

  it('asks again when refreshed, after a navigation', async () => {
    const store = TestBed.inject(WaitingStore);
    TestBed.tick();
    answer();
    await settle();
    store.refresh();
    TestBed.tick();

    expect(http.match((req) => req.url.startsWith(`${Api}/knowledge/claims`))).toHaveLength(1);
  });
});
