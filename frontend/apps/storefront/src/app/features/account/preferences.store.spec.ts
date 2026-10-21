import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import type { PreferencesView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { AnalyticsConsent } from '@tendero/shared-telemetry';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CustomerLink } from '../../data-access/customer-link';
import { ShopperAccountService } from '../../data-access/shopper-account.service';
import { PreferencesStore } from './preferences.store';

const view = (culture: string, marketing: PreferencesView['marketing'] = null): PreferencesView => ({
  culture,
  cultureChosen: false,
  marketing,
  marketingWording: 'marketing-2026-10',
});

/** The shopper's preferences (P15-6): the language kept and shown at once, one consent, analytics in the browser. */
describe('the preferences store', () => {
  let went: string[];
  let consent: string[];
  let sent: unknown[];

  beforeEach(() => {
    went = [];
    consent = [];
    sent = [];
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        PreferencesStore,
        { provide: AuthStore, useValue: { isSignedIn: () => true } },
        { provide: CustomerLink, useValue: { ensure: async () => undefined } },
        { provide: TranslocoService, useValue: { translate: (key: string) => key } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        {
          provide: Router,
          useValue: { url: '/es/account/preferences', navigateByUrl: async (url: string) => void went.push(url) },
        },
        {
          provide: AnalyticsConsent,
          useValue: { state: signal('unasked'), grant: () => consent.push('grant'), deny: () => consent.push('deny') },
        },
        {
          provide: ShopperAccountService,
          useValue: {
            preferences: () => of(view('es')),
            chooseCulture: (culture: string) => (sent.push(['culture', culture]), of(view(culture))),
            setMarketing: (given: boolean) => (
              sent.push(['marketing', given]),
              of(view('es', { given, at: '2026-10-21T10:00:00Z', wording: 'marketing-2026-10' }))
            ),
          },
        },
      ],
    });
    return TestBed.inject(PreferencesStore);
  }

  /** Kept by the account first, then the page itself switches to the language chosen. */
  it('keeps the language chosen and then shows the page in it', async () => {
    const store = opened();
    await store.load();

    await store.chooseCulture('en');

    expect([sent, store.culture(), went]).toEqual([[['culture', 'en']], 'en', ['/en/account/preferences']]);
  });

  it('asks nothing when the language chosen is the one it already is', async () => {
    const store = opened();
    await store.load();

    await store.chooseCulture('es');

    expect([sent, went]).toEqual([[], []]);
  });

  it('records marketing given, with when, and is off until then', async () => {
    const store = opened();
    await store.load();
    const before = [store.marketingGiven(), store.marketingAt()];

    await store.setMarketing(true);

    expect([before, store.marketingGiven(), store.marketingAt()]).toEqual([
      [false, null],
      true,
      '2026-10-21T10:00:00Z',
    ]);
  });

  /** Analytics consent stays in this browser: the page changes it there, and the server is never asked. */
  it('changes analytics consent in the browser and sends nothing', () => {
    const store = opened();

    store.setAnalytics(true);
    store.setAnalytics(false);

    expect([consent, sent]).toEqual([['grant', 'deny'], []]);
  });
});
