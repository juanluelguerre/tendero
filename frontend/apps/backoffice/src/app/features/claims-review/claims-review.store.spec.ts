import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ClaimForReview } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ClaimsReviewStore } from './claims-review.store';

const Claims = 'http://api/api/knowledge/claims';
const Products = 'http://api/api/catalog/products';

const claim = (claimId: string) => ({ claimId, productId: 'p1', support: 0.5 }) as unknown as ClaimForReview;

/** The claims queue's shared state (ADR 0047): the filter, the page and the open claim in the URL, the queue, the decisions. */
describe('the claims review store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ClaimsReviewStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  function answer() {
    http
      .match((req) => req.url === Claims)
      .forEach((req) =>
        req.flush({ items: [claim('c1'), claim('c2')], total: 2, counts: [{ support: 'Weak', count: 2 }] }),
      );
    http
      .match((req) => req.url === Products)
      .forEach((req) => req.flush({ items: [{ productId: 'p1', name: 'Mochila' }], total: 1 }));
  }

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(ClaimsReviewStore);
    TestBed.tick();
    answer();
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  /** The weakest support is what a person is for (phase 10); a link saved before the enum said `all`, in lowercase. */
  it('opens on the weakly supported claims, and reads All in any case', async () => {
    const weak = await at('/');
    const first = [weak.filterKey(), weak.productName(claim('c1'))];
    await TestBed.inject(Router).navigateByUrl('/?support=all');
    TestBed.tick();

    expect([...first, weak.filterKey()]).toEqual(['Weak', 'Mochila', 'All']);
  });

  /**
   * An empty weak queue is not an empty review: when claims wait under All, the
   * page says so and points there. The template used to compare the list with
   * itself, so that sentence could never be shown.
   */
  it('knows when the weak queue is empty but claims wait under All', async () => {
    await TestBed.inject(Router).navigateByUrl('/');
    const store = TestBed.inject(ClaimsReviewStore);
    TestBed.tick();
    http
      .match((req) => req.url === Claims)
      .forEach((req) =>
        req.flush({
          items: [],
          total: 0,
          counts: [
            { support: 'Weak', count: 0 },
            { support: 'All', count: 3 },
          ],
        }),
      );
    http.match((req) => req.url === Products).forEach((req) => req.flush({ items: [], total: 0 }));
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.claims().length, store.othersWaiting()]).toEqual([0, true]);
  });

  /** Deciding moves on to the next claim, and the queue is read again: it is the server's. */
  it('opens the next claim after a decision, and asks for the queue again', async () => {
    const store = await at('/?claim=c1');
    store.decide(claim('c1'), 'approve', 'pane');
    http.expectOne(`${Claims}/c1/approve`).flush({});
    // The queue is being asked again, so the app is not stable: one turn for the navigation.
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([TestBed.inject(Router).url, http.match((req) => req.url === Claims).length]).toEqual(['/?claim=c2', 1]);
  });
});
