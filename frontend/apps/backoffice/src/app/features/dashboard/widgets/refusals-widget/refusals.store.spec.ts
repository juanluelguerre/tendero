import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { AgentDenialsSummary } from '@tendero/shared-api';
import { CultureStore, provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { RefusalsStore } from './refusals.store';

const WORDS = {
  dashboard: {
    refusals: {
      denied: 'Refused',
      rule: { 'mandate-expired': 'Mandate expired', denied: 'Refused by a rule' },
    },
  },
};

const denials = (byReason: AgentDenialsSummary['byReason']): AgentDenialsSummary => ({
  period: 'Week',
  total: byReason.reduce((sum, row) => sum + row.count, 0),
  byReason,
  byAgent: [],
  recent: [],
});

/** What the shop refused agents (R-9, D-7): one bar per rule, named in the reader's language, and nothing to rank when nothing was refused. */
describe('the refusals store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        DashboardSources,
        DashboardRefresh,
        RefusalsStore,
      ],
    });
    TestBed.inject(CultureStore).use('en');
    http = TestBed.inject(HttpTestingController);
  });

  async function answered(body: AgentDenialsSummary) {
    const store = TestBed.inject(RefusalsStore);
    TestBed.runInInjectionContext(() => store.followPeriod(signal<DashboardPeriod>('Week')));
    TestBed.tick();
    for (const request of http.match((req) => req.url.includes('/i18n/'))) request.flush(WORDS);
    http.expectOne('http://api/api/dashboard/agent-denials?period=Week').flush(body);
    await TestBed.inject(ApplicationRef).whenStable();
    return store;
  }

  it('names each rule in the reader’s words, and a rule with no words of its own as a refusal', async () => {
    const store = await answered(
      denials([
        { reason: 'mandate-expired', count: 5 },
        { reason: 'a-rule-added-later', count: 2 },
      ]),
    );
    const chart = store.chart();

    expect([chart?.categories, chart?.series[0]?.values]).toEqual([
      ['Mandate expired', 'Refused by a rule'],
      [5, 2],
    ]);
  });

  it('has no chart when nothing was refused', async () => {
    const store = await answered(denials([]));

    expect(store.chart()).toBeNull();
  });
});
