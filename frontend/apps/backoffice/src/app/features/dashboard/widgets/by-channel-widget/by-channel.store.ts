import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { SalesMix } from '@tendero/shared-api';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { shareOf } from '../figures';

function through(mix: SalesMix | undefined, channel: 'Web' | 'Mcp' | 'Ucp') {
  return mix?.byChannel.find((row) => row.channel === channel) ?? { orders: 0, revenue: 0 };
}

/**
 * By channel's state (ADR 0047): the period's orders by the channel each one
 * recorded when it was placed (R-9, D-7) — the storefront against agents, two
 * parts of one whole with agents in clay, and agents split between MCP and UCP
 * in words. Orders placed before the channel was recorded are their own line,
 * never the storefront's.
 */
export const ByChannelStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      mix: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.mix(params.period, at) }),
      /** The words as a signal, so the legend follows a change of language. */
      _words: translateObjectSignal('dashboard.byChannel'),
    };
  }),
  withComputed((store) => {
    const web = computed(() => through(store.mix.value(), 'Web'));
    const agents = computed(() => {
      const [mcp, ucp] = [through(store.mix.value(), 'Mcp'), through(store.mix.value(), 'Ucp')];
      return { orders: mcp.orders + ucp.orders, revenue: mcp.revenue + ucp.revenue };
    });
    return {
      /** Null when nothing was sold through a known channel: there is no whole to share. */
      chart: computed<BarChartData | null>(() => {
        if (web().orders + agents().orders === 0) return null;
        const words = store._words() as Partial<Record<'people' | 'agents' | 'orders', string>>;
        return {
          categories: [words.orders ?? ''],
          series: [
            { key: 'people', label: words.people ?? '', role: 'data', values: [web().orders] },
            { key: 'agents', label: words.agents ?? '', role: 'agent', values: [agents().orders] },
          ],
        };
      }),
      agentsBy: computed(() => ({
        mcp: through(store.mix.value(), 'Mcp').orders,
        ucp: through(store.mix.value(), 'Ucp').orders,
      })),
      unrecorded: computed(() => store.mix.value()?.unrecorded.orders ?? 0),
      agentShare: computed(() => shareOf(agents().orders, web().orders + agents().orders)),
      revenueShare: computed(() => shareOf(agents().revenue, web().revenue + agents().revenue)),
    };
  }),
);
