import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ProductSummary } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReviewQueueStore } from './review-queue.store';

const Products = 'http://api/api/catalog/products';

const draft = (code: string) =>
  ({ productId: `id-${code}`, code, name: code, missingCultures: [] }) as unknown as ProductSummary;

/** The review queue's shared state (ADR 0047): the drafts, the open one, and what is being published. */
describe('the review queue store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ReviewQueueStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(ReviewQueueStore);
    TestBed.tick();
    http
      .match((req) => req.url === Products)
      .forEach((req) => req.flush({ items: [draft('A'), draft('B')], total: 2 }));
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('lists the drafts and opens the one the URL names', async () => {
    const store = await at('/?product=B');

    expect([store.drafts().map((item) => item.code), store.selected()?.code]).toEqual([['A', 'B'], 'B']);
  });

  /** Published, it leaves the queue — and the pane goes with it. */
  it('takes a published draft out of the queue and closes its pane', async () => {
    const store = await at('/?product=B');
    store.publish(draft('B'), 'pane');
    const publishing = store.publishing().has('id-B');
    http.expectOne(`${Products}/id-B/publish`).flush({});
    await TestBed.inject(ApplicationRef).whenStable();

    expect([
      publishing,
      store.publishing().has('id-B'),
      store.drafts().map((item) => item.code),
      TestBed.inject(Router).url,
    ]).toEqual([true, false, ['A'], '/']);
  });

  it('says it failed when publishing does, and keeps the draft', async () => {
    const store = await at('/');
    store.publish(draft('A'), 'row');
    http.expectOne(`${Products}/id-A/publish`).flush('down', { status: 503, statusText: 'Unavailable' });

    expect([store.failed(), store.drafts().length]).toEqual([true, 2]);
  });
});
