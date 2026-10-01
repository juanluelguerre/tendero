import { inject } from '@angular/core';
import { signalStore, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/** Best sellers' state (ADR 0047): the period's sales summary, ranked by Ordering. */
export const BestSellersStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      sales: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.sales(params.period, at),
      }),
    };
  }),
);
