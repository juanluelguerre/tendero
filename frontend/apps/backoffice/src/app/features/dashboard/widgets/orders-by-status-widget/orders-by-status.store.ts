import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { BarChartData } from '../../../../charts/chart-contract';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';

/** Orders by status' state (ADR 0047): the period's operations, and the same four counts as rows. */
export const OrdersByStatusStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      operations: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.operations(params.period, at),
      }),
      /** The words as a signal, so the rows' labels follow a change of language. */
      _words: translateObjectSignal('dashboard.ordersByStatus'),
    };
  }),
  withComputed((store) => ({
    chart: computed<BarChartData | null>(() => {
      const ops = store.operations.value();
      if (!ops) return null;
      const words = store._words() as Partial<Record<string, string>>;
      return {
        categories: [
          words['toShip'] ?? '',
          words['inProgress'] ?? '',
          words['paymentFailed'] ?? '',
          words['cancelledCount'] ?? '',
        ],
        series: [
          {
            key: 'orders',
            label: words['title'] ?? '',
            role: 'data',
            values: [ops.toShip, ops.inProgress, ops.paymentFailed, ops.cancelled],
          },
        ],
      };
    }),
  })),
);
