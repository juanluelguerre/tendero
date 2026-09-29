import { inject } from '@angular/core';
import { signalStore, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/** What agents buy, as state (ADR 0047): Ordering's half of the agents' story for the period. */
export const AgentSalesStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      sales: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.agentSales(params.period, at) }),
    };
  }),
);
