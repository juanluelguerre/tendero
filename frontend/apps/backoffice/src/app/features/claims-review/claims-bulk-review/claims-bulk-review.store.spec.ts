import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ClaimForReview } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ClaimsBulkReviewStore } from './claims-bulk-review.store';

const Claims = 'http://api/api/knowledge/claims';

const claim = (claimId: string, isWeak = false) =>
  ({ claimId, productId: 'p1', attributeCode: claimId, value: 'v', isWeak, quotes: [] }) as unknown as ClaimForReview;

/**
 * A product's claims reviewed together (Steward spec, *The bulk review of
 * claims*): every row seen and ticked but the weak ones, which are never
 * approved together — the way on for them is to open each.
 */
describe('the claims bulk review store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ClaimsBulkReviewStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function reviewing(claims: ClaimForReview[]) {
    const store = TestBed.inject(ClaimsBulkReviewStore);
    store.load('p1');
    TestBed.tick();
    const asked = http.expectOne((req) => req.url === Claims);
    asked.flush({ items: claims, total: claims.length, page: 1, pageSize: 100, counts: [] });
    await TestBed.inject(ApplicationRef).whenStable();
    return { store, asked };
  }

  it("asks for the product's claims and ticks all but the weak", async () => {
    const { store, asked } = await reviewing([claim('a'), claim('b', true), claim('c')]);

    expect([asked.request.params.get('product'), asked.request.params.get('support')]).toEqual(['p1', 'All']);
    expect(store.rows().map((row) => [row.ticked, row.tickable])).toEqual([
      [true, true],
      [false, false],
      [true, true],
    ]);
    expect([store.together(), store.weak(), store.selected(), store.firstWeak()?.claimId]).toEqual([2, 1, 2, 'b']);
  });

  it('approves only the ticked, and shows each row its outcome', async () => {
    const { store } = await reviewing([claim('a'), claim('b', true), claim('c')]);
    store.toggle('c');
    store.approve();
    const approval = http.expectOne(`${Claims}/approve`);
    expect(approval.request.body).toEqual({ ids: ['a'] });
    approval.flush({ rows: [{ claimId: 'a', outcome: 'Approved' }] });

    expect(store.rows().map((row) => row.outcome)).toEqual(['Approved', null, null]);
  });

  /** A product with weak claims only: nothing goes together, the weak one is the way on. */
  it('offers nothing to approve when every claim is weak', async () => {
    const { store } = await reviewing([claim('a', true)]);
    store.approve();

    expect([store.canApprove(), http.match(`${Claims}/approve`).length]).toEqual([false, 0]);
  });
});
