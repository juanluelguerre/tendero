import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/**
 * Sell-through's state (ADR 0047): for the period, sold over sold and left, by
 * SKU (R-9, D-7) — the five that move most and the five that sit, as Inventory
 * ranked them from its stock and the pace it reads from Ordering.
 */
export const SellThroughStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      stock: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.stockHealth(params.period, at) }),
    };
  }),
  withComputed((store) => ({
    moves: computed(() => store.stock.value()?.fastest ?? []),
    sits: computed(() => store.stock.value()?.slowest ?? []),
  })),
);
