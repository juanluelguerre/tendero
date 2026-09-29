import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignService } from '../data-access/campaign.service';
import { CampaignsStore } from './campaigns.store';

const campaign = (code: string, overrides: Partial<CampaignView> = {}): CampaignView => ({
  code,
  name: code,
  line: '',
  kind: 'Event',
  scope: 'Shop',
  categoryCode: null,
  phase: 'Running',
  startsAt: '2026-10-01T00:00:00+02:00',
  endsAt: '2026-10-07T23:59:00+02:00',
  priority: 100,
  holds: [],
  imageId: null,
  imageAlt: null,
  ...overrides,
});

/** The campaigns near now, read once per language for every page that draws one (R-8; ADR 0047). */
describe('the campaigns store', () => {
  let answers: (() => ReturnType<CampaignService['list']>)[];
  let asked: number;

  beforeEach(() => {
    asked = 0;
    answers = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: CampaignService, useValue: { list: () => answers[asked++]() } },
      ],
    });
  });

  it('leads with the first shop-wide campaign and finds any by its code, in any case', () => {
    answers.push(() => of({ items: [campaign('KITCHEN_WEEK', { scope: 'Category', categoryCode: 'KITCHEN' }), campaign('OTONO')] }));
    const store = TestBed.inject(CampaignsStore);
    TestBed.tick();

    expect([store.leading()?.code, store.named('kitchen_week')?.code, store.named('NONE')]).toEqual(['OTONO', 'KITCHEN_WEEK', null]);
  });

  /** A campaign is never worth an error on a page: a failed read is an empty list, and a settled one. */
  it('reads a failure as no campaigns, and says it has answered', () => {
    answers.push(() => throwError(() => new Error('down')));
    const store = TestBed.inject(CampaignsStore);
    TestBed.tick();

    expect([store.all(), store.leading(), store.settled()]).toEqual([[], null, true]);
  });

  it('asks again when a clock reaches zero, because the phase has changed', async () => {
    answers.push(() => of({ items: [campaign('SOON', { phase: 'Upcoming' })] }), () => of({ items: [campaign('SOON')] }));
    const store = TestBed.inject(CampaignsStore);
    TestBed.tick();
    store.refresh();
    TestBed.tick();
    await new Promise((settled) => setTimeout(settled));

    expect([asked, store.leading()?.code]).toEqual([2, 'SOON']);
  });
});
