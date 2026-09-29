import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { ManagedCampaignView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../campaigns.store';
import { CampaignEditorStore } from './campaign-editor.store';

const saved = (code: string) =>
  ({
    code,
    name: { es: code, en: code },
    line: { es: '', en: '' },
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    startsAt: '2026-10-01T00:00:00Z',
    endsAt: '2026-10-07T23:59:00Z',
    priority: 10,
    promotionCodes: [],
    image: null,
    status: 'Draft',
    phase: 'Upcoming',
    endedAt: null,
    createdAt: '2026-09-28T10:00:00Z',
    updatedAt: '2026-09-28T10:00:00Z',
  }) as unknown as ManagedCampaignView;

/** The editor's own state (ADR 0047): the form, which starts again with every campaign it is given. */
describe('the campaign editor store', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }, CampaignsStore, CampaignEditorStore],
    });
  });

  function editing(campaign: ManagedCampaignView | null) {
    const store = TestBed.inject(CampaignEditorStore);
    const given = signal(campaign);
    TestBed.runInInjectionContext(() => store.follow(given));
    TestBed.tick();
    return { store, given };
  }

  /** While a new campaign's code is not typed by hand, it follows the name; once typed, it is the person's. */
  it("lets a new campaign's code follow its name until it is typed", () => {
    const { store } = editing(null);
    store.editName('en', 'Tendero Days');
    const followed = store.form().code;
    store.editCode('td-26');
    store.editName('en', 'Tendero Days 2026');

    expect([followed, store.form().code]).toEqual(['TENDERO_DAYS', 'TD-26']);
  });

  it('says a saved campaign changed only once the form says something new', () => {
    const { store } = editing(saved('A'));
    const untouched = store.dirty();
    store.editPriority('20');

    expect([untouched, store.dirty()]).toEqual([false, true]);
  });

  /** Another campaign — or a newer answer about this one — starts the form again. */
  it('starts the form again when another campaign arrives', () => {
    const { store, given } = editing(saved('A'));
    store.editPriority('20');
    given.set(saved('B'));
    TestBed.tick();

    expect([store.form().code, store.form().priority, store.dirty()]).toEqual(['B', 10, false]);
  });
});
