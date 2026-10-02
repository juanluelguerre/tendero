import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ProposalView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProposalCardStore } from './proposal-card.store';

const Proposals = 'http://api/api/steward/proposals';

const proposal = (id: string, status = 'Proposed') =>
  ({
    id,
    status,
    title: `Taza ${id}`,
    topic: 'English name',
    reason: `Because ${id}.`,
    changes: [],
  }) as unknown as ProposalView;

/**
 * The card a turn's proposals arrive as (spec, *The panel*): how many and of what
 * kind, the reason, three of them and how many more — read by the turn's ids, so
 * it says what became of them since, and read again after a review.
 */
describe('the proposal card store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ProposalCardStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  it('shows three of the batch and counts the rest, and the ones decided since', async () => {
    const store = TestBed.inject(ProposalCardStore);
    const batch = signal({ ids: ['a', 'b', 'c', 'd', 'e'], revision: 0 });
    TestBed.runInInjectionContext(() => store.follow(batch));
    TestBed.tick();
    http
      .expectOne((req) => req.url === Proposals)
      .flush({
        items: [proposal('a'), proposal('b', 'Applied'), proposal('c'), proposal('d'), proposal('e', 'Rejected')],
        counts: [],
      });
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.count(), store.topic(), store.reason(), store.shown().length, store.more(), store.decided()]).toEqual(
      [5, 'English name', 'Because a.', 3, 2, 2],
    );
  });

  /**
   * After its own review a batch has nothing to review: the card must not offer
   * "Review 4 proposals" beside "4 already decided" (review of S-4, I-1). An
   * approval whose result was never recorded still waits, as the list's To review
   * chip says.
   */
  it('counts as waiting only what still waits for a person', async () => {
    const store = TestBed.inject(ProposalCardStore);
    const batch = signal({ ids: ['a', 'b', 'c', 'd'], revision: 0 });
    TestBed.runInInjectionContext(() => store.follow(batch));
    TestBed.tick();
    http
      .expectOne((req) => req.url === Proposals)
      .flush({
        items: [proposal('a', 'Applied'), proposal('b', 'Approved'), proposal('c', 'Stale'), proposal('d')],
        counts: [],
      });
    await TestBed.inject(ApplicationRef).whenStable();

    expect([store.waiting(), store.decided(), store.failed()]).toEqual([2, 2, false]);
  });

  it('reads the batch again when the panel says something was decided', async () => {
    const store = TestBed.inject(ProposalCardStore);
    const batch = signal({ ids: ['a'], revision: 0 });
    TestBed.runInInjectionContext(() => store.follow(batch));
    TestBed.tick();
    http.expectOne((req) => req.url === Proposals).flush({ items: [proposal('a')], counts: [] });
    batch.set({ ids: ['a'], revision: 1 });
    TestBed.tick();

    expect(http.match((req) => req.url === Proposals)).toHaveLength(1);
  });
});
