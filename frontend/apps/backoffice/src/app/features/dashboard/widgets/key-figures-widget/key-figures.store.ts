import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import type { KeyFigureSources } from './key-figures';

/**
 * Key figures' state (ADR 0047): four reads, each arriving on its own — the
 * sales summary is the widget's reason to exist; the series, the funnel and the
 * operations add a trend or a figure when they come.
 */
export const KeyFiguresStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      sales: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.sales(params.period, at),
      }),
      _series: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.series(params.period, at),
      }),
      _funnel: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.funnel(params.period, at),
      }),
      _operations: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.operations(params.period, at),
      }),
    };
  }),
  withComputed((store) => ({
    /** What has arrived: a read that failed or has not answered simply adds no figure. */
    sources: computed<KeyFigureSources>(() => {
      const value = <T>(read: { hasValue(): boolean; value(): T }) => (read.hasValue() ? read.value() : undefined);
      return {
        sales: value(store.sales),
        series: value(store._series),
        funnel: value(store._funnel),
        operations: value(store._operations),
      };
    }),
  })),
);
