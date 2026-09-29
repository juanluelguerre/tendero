import { computed } from '@angular/core';
import { patchState, signalMethod, signalStoreFeature, withComputed, withMethods, withState } from '@ngrx/signals';
import type { DashboardPeriod } from '../../../data-access/ordering.service';

/**
 * What every dashboard widget's store shares (ADR 0047): the period it shows,
 * following the widget's input. The dashboard owns the period — it is in the
 * URL — and a widget only follows it, so the store never keeps a copy of its own.
 * Until the widget gives it one there is none, and a read asks nothing.
 */
export function withDashboardPeriod() {
  return signalStoreFeature(
    withState<{ period: DashboardPeriod | null }>({ period: null }),
    withComputed(({ period }) => ({
      /** A read's params: the period, or nothing to ask yet (which keeps the read idle). */
      periodParams: computed(() => {
        const current = period();
        return current ? { period: current } : undefined;
      }),
    })),
    withMethods((store) => ({
      /** Pass the widget's input signal: the store follows it for as long as the widget lives. */
      followPeriod: signalMethod<DashboardPeriod>((period) => patchState(store, { period })),
    })),
  );
}
