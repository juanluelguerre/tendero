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
 * What agents buy (ADR 0047): the period's totals and each agent's share, and
 * what agents bought day by day from the series (R-9, D-7), in clay.
 */
export const AgentSalesStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      sales: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.agentSales(params.period, at),
      }),
      _series: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.series(params.period, at),
      }),
      _culture: inject(CultureStore),
      _words: translateObjectSignal('dashboard.agentSales'),
    };
  }),
  withComputed((store) => ({
    /** Null until the series answers, or when agents bought nothing in the period. */
    byDay: computed<BarChartData | null>(() => {
      const series = store._series.value();
      if (!series || series.current.every((point) => point.agentRevenue === 0)) return null;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: bucketLabels(series, store._culture.active()),
        series: [
          {
            key: 'agents',
            label: words['byDay'] ?? '',
            role: 'agent',
            values: series.current.map((point) => point.agentRevenue),
          },
        ],
      };
    }),
  })),
);
