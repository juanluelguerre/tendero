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
});
