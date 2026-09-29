import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/** What the shop refused agents, as state (ADR 0047): Accounts' half, ranked by rule. */
export const RefusalsStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      denials: refreshedResource({ params: store.periodParams, stream: (params, at) => sources.agentDenials(params.period, at) }),
      /** The rules' words as a signal, so the rows follow a change of language. */
      _rules: translateObjectSignal('dashboard.refusals.rule'),
      _words: translateObjectSignal('dashboard.refusals'),
    };
  }),
  withComputed((store) => ({
    chart: computed<BarChartData | null>(() => {
      const denials = store.denials.value();
      if (!denials || denials.byReason.length === 0) return null;
      const rules = store._rules() as Partial<Record<string, string>>;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: denials.byReason.map((row) => rules[row.reason] ?? rules['denied'] ?? row.reason),
        series: [{ key: 'refused', label: words['denied'] ?? '', role: 'data', values: denials.byReason.map((row) => row.count) }],
      };
    }),
  })),
);
