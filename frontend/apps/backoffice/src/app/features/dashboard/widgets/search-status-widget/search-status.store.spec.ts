import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { SearchStatusStore } from './search-status.store';

/** Search's status (R-9, D-7): each stage, and whether a search answers in full. */
describe('the search status store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        SearchStatusStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function answered(stages: object[]) {
    const store = TestBed.inject(SearchStatusStore);
    TestBed.tick();
    http.expectOne('http://api/api/dashboard/search-status').flush({ stages });
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  /** Meaning switched off is a choice of this process: search answers with words, and says so without alarm. */
  it('says a search is whole when every stage that is on answers', async () => {
    const store = await answered([
      { stage: 'Words', state: 'Up', since: '2026-09-30T09:00:00Z', reason: null },
      { stage: 'Meaning', state: 'Off', since: '2026-09-30T09:00:00Z', reason: null },
    ]);

    expect([store.whole(), store.down().map((stage) => stage.stage)]).toEqual([true, []]);
  });

  it('names the stages that are down', async () => {
    const store = await answered([
      { stage: 'Words', state: 'Up', since: '2026-09-30T09:00:00Z', reason: null },
      { stage: 'Meaning', state: 'Down', since: '2026-09-30T11:40:00Z', reason: 'the vector store does not answer' },
    ]);

    expect([store.whole(), store.down().map((stage) => stage.stage)]).toEqual([false, ['Meaning']]);
  });
});
