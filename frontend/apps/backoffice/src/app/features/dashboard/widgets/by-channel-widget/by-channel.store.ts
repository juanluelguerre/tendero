import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { shareOf } from '../figures';

/**
 * By channel's state (ADR 0047): the period's orders shared between people and
 * agents — two parts of one whole, agents in clay. The surface an agent came
 * through (MCP or UCP) arrives with Plan 3's data.
 */
export const ByChannelStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      sales: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.agentSales(params.period, at),
      }),
      /** The words as a signal, so the legend follows a change of language. */
      _words: translateObjectSignal('dashboard.byChannel'),
    };
  }),
  withComputed((store) => ({
    /** Null when nothing was sold: there is no whole to share. */
    chart: computed<BarChartData | null>(() => {
      const sales = store.sales.value();
      if (!sales || sales.orders === 0) return null;
      const words = store._words() as Partial<Record<'people' | 'agents' | 'orders', string>>;
      return {
        categories: [words.orders ?? ''],
        series: [
          { key: 'people', label: words.people ?? '', role: 'data', values: [sales.orders - sales.agentOrders] },
          { key: 'agents', label: words.agents ?? '', role: 'agent', values: [sales.agentOrders] },
        ],
      };
    }),
    agentShare: computed(() => {
      const sales = store.sales.value();
      return sales ? shareOf(sales.agentOrders, sales.orders) : 0;
    }),
    revenueShare: computed(() => {
      const sales = store.sales.value();
      return sales ? shareOf(sales.agentRevenue, sales.revenue) : 0;
    }),
  })),
);
