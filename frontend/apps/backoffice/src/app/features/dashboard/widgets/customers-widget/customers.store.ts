import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/**
 * Customers' state (ADR 0047): the buyers who were new against those who came
 * back — one whole, the returning ones in the muted colour of the base and the
 * new ones in the data colour of the change.
 */
export const CustomersStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      funnel: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.funnel(params.period, at) }),
      _words: translateObjectSignal('dashboard.customers'),
    };
  }),
  withComputed((store) => ({
    /** Null when nobody bought: there is no whole to share. */
    chart: computed<BarChartData | null>(() => {
      const funnel = store.funnel.value();
      if (!funnel || funnel.newCustomers + funnel.returningCustomers === 0) return null;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: [words['buyers'] ?? ''],
        series: [
          { key: 'new', label: words['newCustomers'] ?? '', role: 'data', values: [funnel.newCustomers] },
          { key: 'returning', label: words['returningCustomers'] ?? '', role: 'previous', values: [funnel.returningCustomers] },
        ],
      };
    }),
  })),
);
