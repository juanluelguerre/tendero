import { computed, inject } from '@angular/core';
import { translateObjectSignal } from '@jsverse/transloco';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { withDashboardPeriod } from '../../sources/with-dashboard-period';
import { toBarChart } from './sales-series';

/**
 * Sales by day's state (ADR 0047): the series on the shop's clock (ADR 0046),
 * as a chart labelled in the reader's culture and in the words of the window
 * it draws.
 */
export const SalesByDayStore = signalStore(
  withDashboardPeriod(),
  withProps((store) => {
    const sources = inject(DashboardSources);
    return {
      series: refreshedResource({
        params: store.periodParams,
        stream: (params, at) => sources.series(params.period, at),
      }),
      _culture: inject(CultureStore),
      /** A signal of the words, so the legend follows a change of language rather than keeping the first. */
      _words: translateObjectSignal('dashboard.salesByDay'),
    };
  }),
  withComputed((store) => ({
    chart: computed(() => {
      const series = store.series.value();
      if (!series) return null;
      // Until the language arrives there are no words yet; the signal fills them in when it does.
      const words = store._words() as Partial<Record<'current' | 'previous', Record<string, string>>>;
      const labels = { current: words.current?.[series.period] ?? '', previous: words.previous?.[series.period] ?? '' };
      return toBarChart(series, store._culture.active(), labels);
    }),
  })),
);
