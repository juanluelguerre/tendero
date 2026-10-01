import { computed, inject } from '@angular/core';
import {
  patchState,
  signalMethod,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import type { BrowsingHistoryView } from '@tendero/shared-api';
import { AuthStore } from '@tendero/shared-auth';
import { Telemetry } from '@tendero/shared-telemetry';
import { firstValueFrom, type Observable } from 'rxjs';
import { BrowsingHistoryService } from '../data-access/browsing-history.service';
import { CustomerLink } from '../data-access/customer-link';
import {
  EmptyTrail,
  hasTrail,
  shownOf,
  switched,
  withEntry,
  withoutEntry,
  type TrailKind,
  type TrailSnapshot,
} from './trail-rules';
import { TrailStorage } from './trail-storage';

interface TrailState {
  /** A guest's trail, as this browser keeps it. */
  readonly _guest: TrailSnapshot;
  /** The account's trail once it has answered; null while signed out or not yet read. */
  readonly _account: TrailSnapshot | null;
}

/**
 * The shopper's trail — what they viewed and searched — wherever it lives (R-8,
 * H-3 and H-3b; docs/privacy.md; ADR 0047). **As Amazon does it**: a guest's in
 * this browser, never sent anywhere; a signed-in customer's in their account,
 * kept by Accounts and following them to every device. At sign-in the browser's
 * trail is sent once and cleared here, so there is one trail, not two.
 *
 * One store for both, so the home's row, *Tu historial* and the pages that
 * record a view or a search never ask which kind of shopper this is. The rules
 * are `trail-rules.ts`'s, the storage `TrailStorage`'s, the account's the
 * server's; this only decides which of them is in charge.
 *
 * Everything here is a convenience and fails like one: a request that fails
 * leaves the trail as it was, and storage that throws leaves no trail at all.
 */
export const TrailStore = signalStore(
  { providedIn: 'root' },
  withState<TrailState>(() => ({ _guest: inject(TrailStorage).read(new Date()), _account: null })),
  withProps(() => ({
    _signedIn: inject(AuthStore).isSignedIn,
    _history: inject(BrowsingHistoryService),
    _customer: inject(CustomerLink),
    _storage: inject(TrailStorage),
    _telemetry: inject(Telemetry),
  })),
  withComputed(({ _signedIn, _guest, _account }) => {
    /** The whole trail as kept: up to 50 products and 20 searches, most recent first. */
    const kept = computed<TrailSnapshot>(() => (_signedIn() ? (_account() ?? EmptyTrail) : _guest()));
    /** What the home's row shows: the last 8 products and 5 searches. */
    const shown = computed(() => shownOf(kept(), new Date()));
    return {
      kept,
      shown,
      enabled: computed(() => kept().enabled),
      /** Whether there is anything to show; a first visit draws no empty box. */
      any: computed(() => hasTrail(shown())),
    };
  }),
  withMethods((store) => {
    const keepGuest = (trail: TrailSnapshot): void => {
      patchState(store, { _guest: trail });
      store._storage.write(trail);
    };

    /** Whether the account answered. */
    const remote = async (call: () => Observable<BrowsingHistoryView>): Promise<boolean> => {
      try {
        await store._customer.ensure();
        const view = await firstValueFrom(call());
        if (!store._signedIn()) return false;

        patchState(store, { _account: snapshotOf(view) });
        // Switched off in the account is switched off in this browser too (docs/privacy.md).
        if (!view.enabled && store._guest().enabled) keepGuest(switched(store._guest(), false));
        return true;
      } catch {
        // The trail stays as it was: a convenience that failed is not an error to show.
        return false;
      }
    };

    const change = (
      local: (trail: TrailSnapshot) => TrailSnapshot,
      call: () => Observable<BrowsingHistoryView>,
      allowed: () => boolean = () => true,
    ): void => {
      if (!store._signedIn()) {
        keepGuest(local(store._guest()));
        return;
      }
      if (allowed()) void remote(call);
    };

    const record = (kind: TrailKind, key: string): void =>
      change(
        (trail) => withEntry(trail, kind, key, new Date()),
        () => (kind === 'View' ? store._history.recordView(key) : store._history.recordSearch(key)),
        // Switched off in the account: not even asked. A guest's switch is the rule's to check.
        () => store._account()?.enabled !== false,
      );

    return {
      recordView(code: string): void {
        record('View', code);
      },
      recordSearch(text: string): void {
        record('Search', text);
      },
      /** One entry removed — a product or a search. */
      forget(kind: TrailKind, key: string): void {
        store._telemetry.trackEvent('trail_cleared', { scope: 'one' });
        change(
          (trail) => withoutEntry(trail, kind, key),
          () => store._history.forget(kind, key),
        );
      },
      /** Everything removed; the switch stays where it was. */
      clear(): void {
        store._telemetry.trackEvent('trail_cleared', { scope: 'all' });
        change(
          (trail) => ({ ...trail, views: [], searches: [] }),
          () => store._history.clear(),
        );
      },
      /**
       * On or off. Off forgets what was kept in the same change, and from then on
       * nothing is recorded — in the account, and in this browser too.
       */
      setEnabled(enabled: boolean): void {
        keepGuest(switched(store._guest(), enabled));
        if (store._signedIn()) void remote(() => store._history.setEnabled(enabled));
      },
      /**
       * Signed in: the customer first (Accounts keeps nothing for an unlinked
       * caller), then the browser's trail merged once and cleared here — or, with
       * nothing to merge, the account's trail read.
       */
      async _startAccount(): Promise<void> {
        const guest = store._guest();
        const merging = guest.enabled && hasTrail(guest);

        const answered = await remote(() =>
          merging
            ? store._history.merge({
                views: guest.views.map((entry) => ({ code: entry.key, at: entry.at })),
                searches: guest.searches.map((entry) => ({ text: entry.key, at: entry.at })),
              })
            : store._history.get(),
        );

        if (answered && merging) keepGuest({ ...EmptyTrail, enabled: store._guest().enabled });
      },
    };
  }),
  withHooks({
    onInit(store) {
      // Signing in brings the account's trail; signing out forgets it here.
      signalMethod<boolean>((signedIn) => {
        if (signedIn) void store._startAccount();
        else patchState(store, { _account: null });
      })(store._signedIn);
    },
  }),
);

function snapshotOf(view: BrowsingHistoryView): TrailSnapshot {
  return {
    enabled: view.enabled,
    views: view.views.map((entry) => ({ key: entry.code, at: entry.at })),
    searches: view.searches.map((entry) => ({ key: entry.text, at: entry.at })),
  };
}
