import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { UndoStore } from '../../worklist/undo/undo.store';
import { CampaignsStore } from './campaigns.store';

const Calendar = 'http://api/api/pricing/campaign-calendar';

function campaign(
  code: string,
  status: ManagedCampaignView['status'],
  phase: ManagedCampaignView['phase'],
): ManagedCampaignView {
  return {
    code,
    name: { es: code, en: code },
    line: { es: 'Lema', en: 'Line' },
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-10-01T00:00:00Z',
    endsAt: '2026-10-07T23:59:00Z',
    priority: 10,
    promotionCodes: [],
    image: null,
    status,
    phase,
    endedAt: null,
    createdAt: '2026-09-28T10:00:00Z',
    updatedAt: '2026-09-28T10:00:00Z',
  } as ManagedCampaignView;
}

/** The campaign calendar's shared state (ADR 0047): the filter and the open campaign in the URL, the calendar, the changes. */
describe('the campaigns store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        CampaignsStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const store = TestBed.inject(CampaignsStore);
    TestBed.tick();
    http
      .match(Calendar)
      .forEach((req) => req.flush([campaign('ON', 'Scheduled', 'Running'), campaign('DRAFT', 'Draft', 'Upcoming')]));
    http.match('http://api/api/pricing/promotions').forEach((req) => req.flush({ items: [] }));
    http.match((req) => req.url === 'http://api/api/catalog/categories').forEach((req) => req.flush({ items: [] }));
    await new Promise((resolve) => setTimeout(resolve));
    return store;
  }

  it('opens on what is running', async () => {
    const store = await at('/');

    expect([store.filterKey(), store.campaigns().map((one) => one.code)]).toEqual(['running', ['ON']]);
  });

  /** Scheduling offers Undo, which is unscheduling — the reverse on the server. */
  it('schedules a campaign and offers to unschedule it', async () => {
    const store = await at('/?status=all');
    store.move(campaign('DRAFT', 'Draft', 'Upcoming'), 'schedule');
    http.expectOne(`${Calendar}/DRAFT/schedule`).flush({ campaign: campaign('DRAFT', 'Scheduled', 'Upcoming') });
    TestBed.inject(UndoStore).take();

    expect(http.match(`${Calendar}/DRAFT/unschedule`)).toHaveLength(1);
  });

  it("keeps the server's refusal with the campaign it was about", async () => {
    const store = await at('/?status=all');
    store.end(campaign('ON', 'Scheduled', 'Running'));
    http.expectOne(`${Calendar}/ON/end`).flush('Already ended.', { status: 409, statusText: 'Conflict' });

    expect([store.refusal(), store.work()]).toEqual([{ code: 'ON', message: 'Already ended.' }, null]);
  });
});
