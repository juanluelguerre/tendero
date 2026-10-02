import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ProposalView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProposalReviewStore } from './proposal-review.store';

const Proposals = 'http://api/api/steward/proposals';

const proposal = (id: string, status = 'Proposed') =>
  ({ id, status, subject: id.toUpperCase(), title: id, topic: 'English name', changes: [] }) as unknown as ProposalView;

/**
 * The review (spec, *The review*): every proposal of a batch as a row, ticked by
 * default, any of them unticked; Approve runs the ticked, Reject the unticked
 * asks why; each row then shows its own result.
 */
describe('the proposal review store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ProposalReviewStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function reviewing(rows: ProposalView[]) {
    const store = TestBed.inject(ProposalReviewStore);
    store.load(rows.map((row) => row.id));
    TestBed.tick();
    const asked = http.expectOne((req) => req.url === Proposals);
    asked.flush({ items: rows, total: rows.length, page: 1, pageSize: 20, counts: [] });
    await TestBed.inject(ApplicationRef).whenStable();
    return { store, asked };
  }

  /** Only what still waits can be decided; the rest shows what became of it. */
  it('asks for the batch by its ids and ticks every proposal still waiting', async () => {
    const { store, asked } = await reviewing([proposal('a'), proposal('b'), proposal('c', 'Applied')]);

    expect(asked.request.params.getAll('ids')).toEqual(['a', 'b', 'c']);
    expect([store.rows().map((row) => row.ticked), store.selected(), store.waiting()]).toEqual([
      [true, true, false],
      2,
      2,
    ]);
  });

  it('approves only the ticked ones and shows each row its result', async () => {
    const { store } = await reviewing([proposal('a'), proposal('b'), proposal('c')]);
    store.toggle('b');
    store.approve();
    const approval = http.expectOne(`${Proposals}/approve`);
    expect(approval.request.body).toEqual({ ids: ['a', 'c'] });
    approval.flush({
      rows: [
        { id: 'a', outcome: 'Applied', reason: null },
        { id: 'c', outcome: 'Stale', reason: null },
      ],
    });

    expect(store.rows().map((row) => row.outcome?.outcome ?? null)).toEqual(['Applied', null, 'Stale']);
    expect([store.selected(), store.unticked()]).toEqual([0, 1]);
  });

  it('rejects the unticked ones with the reason given', async () => {
    const { store } = await reviewing([proposal('a'), proposal('b')]);
    store.toggle('a');
    store.rejectUnticked('Too literal.');

    expect(http.expectOne(`${Proposals}/reject`).request.body).toEqual({ ids: ['a'], reason: 'Too literal.' });
  });

  it('offers nothing to approve when every row is unticked', async () => {
    const { store } = await reviewing([proposal('a')]);
    store.toggle('a');
    store.approve();

    expect([store.canApprove(), http.match(`${Proposals}/approve`).length]).toEqual([false, 0]);
  });
});
