import { computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { signalStore, withComputed, withMethods, withProps } from '@ngrx/signals';
import type { PreferencesView } from '@tendero/shared-api';
import { AnalyticsConsent } from '@tendero/shared-telemetry';
import { withAccountSection } from '../../account-section/with-account-section';
import { ShopperAccountService } from '../../data-access/shopper-account.service';
import { ShopLinks } from '../../shop-links';

/**
 * The preferences page's own state (P15-6; board 05 of the account's mock-ups;
 * ADR 0047): the language the shop speaks to the shopper in, one marketing
 * consent kept by the server with its date and wording, and analytics consent,
 * which stays in this browser and is only shown and changed here.
 */
export const PreferencesStore = signalStore(
  withAccountSection<PreferencesView>('preferences.failed'),
  withProps(() => ({
    _account: inject(ShopperAccountService),
    _router: inject(Router),
    _links: inject(ShopLinks),
    analytics: inject(AnalyticsConsent),
  })),
  withComputed(({ view, analytics }) => ({
    culture: computed(() => view()?.culture ?? null),
    /** Given and not withdrawn. */
    marketingGiven: computed(() => view()?.marketing?.given ?? false),
    /** When the shopper last said, either way; null while they never have. */
    marketingAt: computed(() => view()?.marketing?.at ?? null),
    analyticsAllowed: computed(() => analytics.state() === 'granted'),
  })),
  withMethods((store) => ({
    load: () => store._load(() => store._account.preferences()),

    /** Kept by the account, then shown: the page itself switches to the language chosen. */
    async chooseCulture(culture: string): Promise<void> {
      if (culture === store.culture()) return;
      if (await store._change(() => store._account.chooseCulture(culture)))
        await store._router.navigateByUrl(store._links.sameIn(store._router.url, culture));
    },

    setMarketing: (given: boolean) => store._change(() => store._account.setMarketing(given)),

    setAnalytics(allowed: boolean): void {
      if (allowed) store.analytics.grant();
      else store.analytics.deny();
    },
  })),
);
