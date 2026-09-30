import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { DashboardRefresh } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { CampaignsStore, leadingCampaign } from './campaigns.store';

const campaign = (code: string, overrides: Partial<ManagedCampaignView>): ManagedCampaignView =>
  ({
    code,
    name: { es: code, en: code },
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-10-01T00:00:00+02:00',
    endsAt: '2026-10-08T00:00:00+02:00',
    priority: 10,
    status: 'Scheduled',
    phase: 'Running',
    endedAt: null,
    ...overrides,
  }) as ManagedCampaignView;

/** Campaigns against their baseline (R-9, D-7): the window is Pricing's, the figures Ordering's. */
describe('the campaigns store', () => {
  it('reads the running campaign with the highest priority, else the last that ended, never a draft', () => {
    expect(leadingCampaign([campaign('LOW', { priority: 5 }), campaign('HIGH', { priority: 50 }), campaign('DRAFT', { status: 'Draft', priority: 99 })])?.code).toBe('HIGH');
    expect(
      leadingCampaign([
        campaign('OLD', { phase: 'Over', status: 'Ended', startsAt: '2026-08-25T00:00:00+02:00', endsAt: '2026-09-01T00:00:00+02:00' }),
        campaign('RECENT', { phase: 'Over', status: 'Ended', startsAt: '2026-09-13T00:00:00+02:00', endsAt: '2026-09-20T00:00:00+02:00' }),
        campaign('SOON', { phase: 'Upcoming' }),
      ])?.code,
    ).toBe('RECENT');
    expect(leadingCampaign([campaign('SOON', { phase: 'Upcoming' })])).toBeNull();
  });

  /** Ended before it opened, a campaign never ran: nothing to measure, and a window that closes before it opens (found with real data, 2026-09-30). */
  it('skips a campaign ended before it opened', () => {
    expect(
      leadingCampaign([
        campaign('NEVER', { phase: 'Over', status: 'Ended', startsAt: '2026-09-30T00:00:00+02:00', endedAt: '2026-09-28T23:16:56+00:00', endsAt: '2026-10-07T00:00:00+02:00' }),
        campaign('RAN', { phase: 'Over', status: 'Ended', endsAt: '2026-09-20T00:00:00+02:00', startsAt: '2026-09-13T00:00:00+02:00' }),
      ])?.code,
    ).toBe('RAN');
  });

  describe('against the server', () => {
    let http: HttpTestingController;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: 'http://api' }, DashboardSources, DashboardRefresh, CampaignsStore],
      });
      http = TestBed.inject(HttpTestingController);
    });

    /** A category's campaign is measured on its category; one ended early closes its window when it ended. */
    it('asks Ordering for the campaign window Pricing gave it, and its category', async () => {
      const store = TestBed.inject(CampaignsStore);
      TestBed.tick();
      http.expectOne('http://api/api/pricing/campaign-calendar').flush([
        campaign('KITCHEN', { scope: 'Category', categoryCode: 'COOKWARE', endedAt: '2026-10-05T12:00:00+02:00', status: 'Ended', phase: 'Over' }),
      ]);
      // Not whenStable: the uplift the store asks next is pending, and stability waits for it.
      await new Promise((settled) => setTimeout(settled));
      TestBed.tick();

      const asked = http.expectOne((req) => req.url === 'http://api/api/dashboard/uplift');
      expect([asked.request.params.get('from'), asked.request.params.get('to'), asked.request.params.get('category')]).toEqual([
        '2026-10-01T00:00:00+02:00',
        '2026-10-05T12:00:00+02:00',
        'COOKWARE',
      ]);
      expect(store.campaign()?.code).toBe('KITCHEN');
    });
  });
});
