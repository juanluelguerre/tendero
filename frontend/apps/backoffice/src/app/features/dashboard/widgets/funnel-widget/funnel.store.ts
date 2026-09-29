import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { shareOf } from '../figures';

/** The checkout funnel's state (ADR 0047): the period's carts, and each step as a share of those started. */
export const FunnelStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      funnel: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.funnel(params.period, at) }),
    };
  }),
  withComputed((store) => ({
    /** The steps as bars, each as wide as its share of the carts started. */
    steps: computed(() => {
      const funnel = store.funnel.value();
      if (!funnel) return [];
      return [
        { key: 'started', count: funnel.cartsStarted, share: funnel.cartsStarted === 0 ? 0 : 1 },
        { key: 'checkedOut', count: funnel.checkedOut, share: shareOf(funnel.checkedOut, funnel.cartsStarted) },
        { key: 'abandoned', count: funnel.abandoned, share: shareOf(funnel.abandoned, funnel.cartsStarted) },
        { key: 'stillOpen', count: funnel.stillOpen, share: shareOf(funnel.stillOpen, funnel.cartsStarted) },
      ];
    }),
  })),
);
