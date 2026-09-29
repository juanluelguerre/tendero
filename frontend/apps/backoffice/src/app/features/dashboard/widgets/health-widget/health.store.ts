import { inject } from '@angular/core';
import { signalStore, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/** Health's state (ADR 0047): the period's operations, whose standing against the target the server decided. */
export const HealthStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      operations: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.operations(params.period, at) }),
    };
  }),
);
