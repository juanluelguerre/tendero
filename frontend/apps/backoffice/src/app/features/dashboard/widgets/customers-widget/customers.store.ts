import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { bucketLabels } from '../sales-by-day-widget/sales-series';

/**
 * Customers' state (ADR 0047): the period's buyers, new and returning, from the
 * funnel's totals, and day by day from the series (R-9, D-7) — a buyer is new on
 * the first day the shop sees them and returning after.
 */
export const CustomersStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      funnel: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.funnel(params.period, at) }),
      _series: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.series(params.period, at) }),
      _culture: inject(CultureStore),
      _words: translateObjectSignal('dashboard.customers'),
    };
  }),
  withComputed((store) => ({
    /** Null when nobody bought in the period: a chart of zeros says nothing a sentence would not. */
    byDay: computed<BarChartData | null>(() => {
      const series = store._series.value();
      if (!series || series.current.every((point) => point.newCustomers + point.returningCustomers === 0)) return null;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: bucketLabels(series, store._culture.active()),
        series: [
          { key: 'new', label: words['newCustomers'] ?? '', role: 'data', values: series.current.map((point) => point.newCustomers) },
          { key: 'returning', label: words['returningCustomers'] ?? '', role: 'previous', values: series.current.map((point) => point.returningCustomers) },
        ],
      };
    }),
  })),
);
