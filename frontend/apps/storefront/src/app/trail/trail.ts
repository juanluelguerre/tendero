import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
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

/**
 * The shopper's trail — what they viewed and searched — wherever it lives (R-8,
 * H-3 and H-3b; docs/privacy.md). **As Amazon does it**: a guest's in this
 * browser, never sent anywhere; a signed-in customer's in their account, kept by
 * Accounts and following them to every device. At sign-in the browser's trail is
 * sent once and cleared here, so there is one trail, not two.
 *
 * One service for both, so the home's row, *Tu historial* and the pages that
 * record a view or a search never ask which kind of shopper this is. The rules
 * are `trail-rules.ts`'s, the storage `TrailStorage`'s, the account's the
 * server's; this only decides which of them is in charge.
 *
 * Everything here is a convenience and fails like one: a request that fails
 * leaves the trail as it was, and storage that throws leaves no trail at all.
 */
@Injectable({ providedIn: 'root' })
export class Trail {
  private readonly auth = inject(AuthStore);
  private readonly history = inject(BrowsingHistoryService);
  private readonly customer = inject(CustomerLink);
  private readonly storage = inject(TrailStorage);
  private readonly telemetry = inject(Telemetry);

  private readonly guest = signal<TrailSnapshot>(this.storage.read(new Date()));

  /** The account's trail once it has answered; null while signed out or not yet read. */
  private readonly account = signal<TrailSnapshot | null>(null);

  private readonly signedIn = this.auth.isSignedIn;

  /** The whole trail as kept: up to 50 products and 20 searches, most recent first. */
  readonly kept = computed<TrailSnapshot>(() => (this.signedIn() ? (this.account() ?? EmptyTrail) : this.guest()));

  /** What the home's row shows: the last 8 products and 5 searches. */
  readonly shown = computed(() => shownOf(this.kept(), new Date()));

  readonly enabled = computed(() => this.kept().enabled);

  /** Whether there is anything to show; a first visit draws no empty box. */
  readonly any = computed(() => hasTrail(this.shown()));

  constructor() {
    effect(() => {
      const signedIn = this.signedIn();
      untracked(() => {
        if (signedIn) void this.startAccount();
        else this.account.set(null);
      });
    });
  }

  recordView(code: string): void {
    this.record('View', code);
  }

  recordSearch(text: string): void {
    this.record('Search', text);
  }

  /** One entry removed — a product or a search. */
  forget(kind: TrailKind, key: string): void {
    this.telemetry.trackEvent('trail_cleared', { scope: 'one' });
    this.change(
      (trail) => withoutEntry(trail, kind, key),
      () => this.history.forget(kind, key),
    );
  }

  /** Everything removed; the switch stays where it was. */
  clear(): void {
    this.telemetry.trackEvent('trail_cleared', { scope: 'all' });
    this.change(
      (trail) => ({ ...trail, views: [], searches: [] }),
      () => this.history.clear(),
    );
  }

  /**
   * On or off. Off forgets what was kept in the same change, and from then on
   * nothing is recorded — in the account, and in this browser too.
   */
  setEnabled(enabled: boolean): void {
    this.guest.set(switched(this.guest(), enabled));
    this.storage.write(this.guest());
    if (this.signedIn()) void this.remote(() => this.history.setEnabled(enabled));
  }

  // ---------- Which trail is in charge ----------

  private record(kind: TrailKind, key: string): void {
    this.change(
      (trail) => withEntry(trail, kind, key, new Date()),
      () => (kind === 'View' ? this.history.recordView(key) : this.history.recordSearch(key)),
      // Switched off in the account: not even asked. A guest's switch is the rule's to check.
      () => this.account()?.enabled !== false,
    );
  }

  private change(
    local: (trail: TrailSnapshot) => TrailSnapshot,
    remote: () => Observable<BrowsingHistoryView>,
    allowed: () => boolean = () => true,
  ): void {
    if (!this.signedIn()) {
      this.guest.set(local(this.guest()));
      this.storage.write(this.guest());
      return;
    }
    if (allowed()) void this.remote(remote);
  }

  /**
   * Signed in: the customer first (Accounts keeps nothing for an unlinked
   * caller), then the browser's trail merged once and cleared here — or, with
   * nothing to merge, the account's trail read.
   */
  private async startAccount(): Promise<void> {
    const guest = this.guest();
    const merging = guest.enabled && hasTrail(guest);

    const answered = await this.remote(() =>
      merging
        ? this.history.merge({
            views: guest.views.map((entry) => ({ code: entry.key, at: entry.at })),
            searches: guest.searches.map((entry) => ({ text: entry.key, at: entry.at })),
          })
        : this.history.get(),
    );

    if (answered && merging) {
      this.guest.set({ ...EmptyTrail, enabled: this.guest().enabled });
      this.storage.write(this.guest());
    }
  }

  /** Whether the account answered. */
  private async remote(call: () => Observable<BrowsingHistoryView>): Promise<boolean> {
    try {
      await this.customer.ensure();
      const view = await firstValueFrom(call());
      if (!this.signedIn()) return false;

      this.account.set(snapshotOf(view));
      // Switched off in the account is switched off in this browser too (docs/privacy.md).
      if (!view.enabled && this.guest().enabled) {
        this.guest.set(switched(this.guest(), false));
        this.storage.write(this.guest());
      }
      return true;
    } catch {
      // The trail stays as it was: a convenience that failed is not an error to show.
      return false;
    }
  }
}

function snapshotOf(view: BrowsingHistoryView): TrailSnapshot {
  return {
    enabled: view.enabled,
    views: view.views.map((entry) => ({ key: entry.code, at: entry.at })),
    searches: view.searches.map((entry) => ({ key: entry.text, at: entry.at })),
  };
}
