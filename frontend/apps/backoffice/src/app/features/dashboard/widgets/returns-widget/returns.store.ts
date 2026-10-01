import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { ReturnReason } from '@tendero/shared-api';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/**
 * Returns' state (ADR 0047): the period's operations, and the open returns
 * ranked by reason — in the returns screen's own words, so five reasons have
 * one translation, not two that drift.
 */
export const ReturnsStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      operations: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.operations(params.period, at),
      }),
      _reasons: translateObjectSignal('returns.reason'),
      _words: translateObjectSignal('dashboard.returns'),
    };
  }),
  withComputed((store) => ({
    /** Null when nothing is open: no bars to rank. */
    chart: computed<BarChartData | null>(() => {
      const ops = store.operations.value();
      if (!ops || ops.openReturnsByReason.length === 0) return null;
      const reasons = store._reasons() as Partial<Record<ReturnReason, string>>;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: ops.openReturnsByReason.map((row) => reasons[row.reason] ?? row.reason),
        series: [
          {
            key: 'open',
            label: words['openReturns'] ?? '',
            role: 'data',
            values: ops.openReturnsByReason.map((row) => row.count),
          },
        ],
      };
    }),
  })),
);
