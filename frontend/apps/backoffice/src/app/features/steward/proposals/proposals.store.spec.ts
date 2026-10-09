import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ProposalView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProposalsStore } from './proposals.store';

const Proposals = 'http://api/api/steward/proposals';

const proposal = (id: string) =>
  ({ id, title: `Product ${id}`, status: 'Proposed', changes: [] }) as unknown as ProposalView;

/** The Proposals list's shared state (ADR 0047): the filter, the page and the open proposal in the URL, the rows, the decisions. */
describe('the proposals store', () => {
  let http: HttpTestingController;
  const telemetry = { trackEvent: vi.fn() };

  beforeEach(() => {
    telemetry.trackEvent.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: Telemetry, useValue: telemetry },
        ProposalsStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  function answer() {
    http
      .match((req) => req.url === Proposals && req.method === 'GET')
      .forEach((req) =>
        req.flush({
          items: [proposal('p1'), proposal('p2')],
          total: 2,
          page: 1,
          pageSize: 20,
          counts: [
            { filter: 'ToReview', count: 2 },
            { filter: 'Applied', count: 5 },
            { filter: 'Rejected', count: 0 },
            { filter: 'Failed', count: 1 },
            { filter: 'All', count: 8 },
          ],
        }),
      );
  }

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(ProposalsStore);
    TestBed.tick();
    answer();
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  /** What waits for a person is what the list opens on; a saved link names its filter in any case. */
  it('opens on the proposals to review, and reads a filter by its name in any case', async () => {
    const store = await at('/');
    const first = store.filterKey();
    await TestBed.inject(Router).navigateByUrl('/?filter=failed');
    TestBed.tick();

    expect([first, store.filterKey()]).toEqual(['ToReview', 'Failed']);
  });

  it('asks for the filter shown and counts every chip from the server', async () => {
    await TestBed.inject(Router).navigateByUrl('/?filter=Applied&page=2');
    const store = TestBed.inject(ProposalsStore);
    TestBed.tick();
    const asked = http.expectOne((req) => req.url === Proposals);
    expect([asked.request.params.get('filter'), asked.request.params.get('page')]).toEqual(['Applied', '2']);
    asked.flush({ items: [], total: 0, page: 2, pageSize: 20, counts: [{ filter: 'Applied', count: 5 }] });
    await TestBed.inject(ApplicationRef).whenStable();

    expect(store.filters().map((chip) => [chip.key, chip.count])).toEqual([
      ['ToReview', null],
      ['Applied', 5],
      ['Rejected', null],
      ['Failed', null],
      ['All', null],
    ]);
  });

  /** Approving runs the command as the person, then the list is read again and the next proposal opens. */
  it('approves the open proposal, reads the list again and opens the next', async () => {
    const store = await at('/?proposal=p1');
    store.approve(proposal('p1'));
    const approval = http.expectOne(`${Proposals}/approve`);
    expect(approval.request.body).toEqual({ ids: ['p1'] });
    approval.flush({ rows: [{ id: 'p1', outcome: 'Applied', reason: null }] });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([TestBed.inject(Router).url, http.match((req) => req.url === Proposals).length]).toEqual([
      '/?proposal=p2',
      1,
    ]);
  });

  it('reports a decision taken in the pane as a batch of one', async () => {
    const store = await at('/?proposal=p1');
    store.approve(proposal('p1'));
    http.expectOne(`${Proposals}/approve`).flush({ rows: [{ id: 'p1', outcome: 'Applied', reason: null }] });

    expect(telemetry.trackEvent.mock.calls).toEqual([
      ['proposals_reviewed', { kind: 'proposals', proposed: 1, approved: 1, rejected: 0, unticked: 0 }],
    ]);
  });

  it('reports nothing when the decision in the pane did not go through', async () => {
    const store = await at('/?proposal=p1');
    store.approve(proposal('p1'));
    http.expectOne(`${Proposals}/approve`).flush('taken', { status: 409, statusText: 'Conflict' });

    expect(telemetry.trackEvent).not.toHaveBeenCalled();
  });

  it('rejects with the reason the person gave', async () => {
    const store = await at('/?proposal=p2');
    store.reject(proposal('p2'), 'Too literal.');

    expect(http.expectOne(`${Proposals}/reject`).request.body).toEqual({ ids: ['p2'], reason: 'Too literal.' });
  });

  // ---------- approved with the person's own words ----------

  const altText = (id: string) =>
    ({ ...proposal(id), command: 'SetImageAltText', image: '9f2c' }) as unknown as ProposalView;

  /** The third decision: the words corrected, sent by the key of the change they belong to; then the list moves on as after any approval. */
  it('approves with the corrected words, by the key of each, and opens the next', async () => {
    const store = await at('/?proposal=p1');
    store.approveEdited(proposal('p1'), { english: 'Olive green backpack.' });
    const approval = http.expectOne(`${Proposals}/p1/approve-edited`);
    expect(approval.request.body).toEqual({ edits: { english: 'Olive green backpack.' } });
    approval.flush({ id: 'p1', outcome: 'Applied', reason: null });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([TestBed.inject(Router).url, store.refusal()]).toEqual(['/?proposal=p2', null]);
  });

  /** Words the server refuses leave the proposal open with the reason beside it: the person corrects again, nothing was decided. */
  it('keeps the proposal open and says why when the corrected words are refused', async () => {
    const store = await at('/?proposal=p1');
    store.approveEdited(proposal('p1'), { english: ' ' });
    http
      .expectOne(`${Proposals}/p1/approve-edited`)
      .flush({ id: 'p1', outcome: 'Invalid', reason: 'English must say what the picture shows.' });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([TestBed.inject(Router).url, store.refusal(), store.busy()]).toEqual([
      '/?proposal=p1',
      'English must say what the picture shows.',
      null,
    ]);
    expect(telemetry.trackEvent).not.toHaveBeenCalled();
  });

  /** What became of a proposed alt text is its own event: approved as written, corrected first, or rejected. */
  it('reports what a person did with a proposed alt text, and nothing of the kind for other proposals', async () => {
    const store = await at('/?proposal=p1');

    store.approve(altText('p1'));
    http.expectOne(`${Proposals}/approve`).flush({ rows: [{ id: 'p1', outcome: 'Applied', reason: null }] });
    store.approveEdited(altText('p1'), { english: 'Olive green backpack.' });
    http.expectOne(`${Proposals}/p1/approve-edited`).flush({ id: 'p1', outcome: 'Applied', reason: null });
    store.reject(altText('p1'), 'Not what it shows.');
    http
      .expectOne(`${Proposals}/reject`)
      .flush({ rows: [{ id: 'p1', outcome: 'Rejected', reason: 'Not what it shows.' }] });
    store.approve(proposal('p2'));
    http.expectOne(`${Proposals}/approve`).flush({ rows: [{ id: 'p2', outcome: 'Applied', reason: null }] });

    const decided = telemetry.trackEvent.mock.calls.filter(([name]) => name === 'image_alt_proposal_decided');
    expect(decided).toEqual([
      ['image_alt_proposal_decided', { decision: 'approved' }],
      ['image_alt_proposal_decided', { decision: 'edited' }],
      ['image_alt_proposal_decided', { decision: 'rejected' }],
    ]);
  });

  /** A proposal that did not go through — stale, already decided — was not decided by this person: no event. */
  it('reports no alt-text decision when the approval did not apply', async () => {
    const store = await at('/?proposal=p1');
    store.approve(altText('p1'));
    http.expectOne(`${Proposals}/approve`).flush({ rows: [{ id: 'p1', outcome: 'Stale', reason: null }] });

    expect(telemetry.trackEvent.mock.calls.filter(([name]) => name === 'image_alt_proposal_decided')).toEqual([]);
  });

  // ---------- a batch of alt texts ----------

  /** Five pictures at a time, asked for by a person, in the language of their screen; then the list is read again to show them. */
  it('asks for a batch of five alt texts and reads the list again', async () => {
    const store = await at('/');
    store.describePictures();
    const asked = http.expectOne((req) => req.url === 'http://api/api/steward/alt-text/propose');
    expect([asked.request.body, asked.request.params.get('culture'), store.describing()]).toEqual([
      { count: 5 },
      'en',
      true,
    ]);
    asked.flush({ status: 'Ran', proposals: ['a', 'b'], skipped: 1, failed: 0 });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([store.describing(), store.described(), http.match((req) => req.url === Proposals).length]).toEqual([
      false,
      { status: 'Ran', proposals: ['a', 'b'], skipped: 1, failed: 0 },
      1,
    ]);
  });

  it('stops waiting when the batch could not be asked for, and says nothing was described', async () => {
    const store = await at('/');
    store.describePictures();
    http
      .expectOne((req) => req.url === 'http://api/api/steward/alt-text/propose')
      .flush('down', { status: 503, statusText: 'Unavailable' });

    expect([store.describing(), store.described()]).toEqual([false, null]);
  });
});
