import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../charts/bar-chart';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { toBarChart } from './sales-series';

/** Sales by hour or by day against the period before, on the shop's clock (R-9, ADR 0046): the first chart of the dashboard. */
@Component({
  selector: 'backoffice-sales-by-day-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sales-by-day-widget.html',
  styleUrl: './sales-by-day-widget.css',
})
export class SalesByDayWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);
  /** A signal of the words, so the legend follows a change of language rather than keeping the first. */
  private readonly words = translateObjectSignal('dashboard.salesByDay');

  readonly period = input.required<DashboardPeriod>();

  protected readonly series = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.series(params.period, params.at),
  });

  protected readonly chart = computed(() => {
    const series = this.series.value();
    if (!series) return null;
    // Until the language arrives there are no words yet; the signal fills them in when it does.
    const words = this.words() as Partial<Record<'current' | 'previous', Record<string, string>>>;
    const labels = { current: words.current?.[series.period] ?? '', previous: words.previous?.[series.period] ?? '' };
    return toBarChart(series, this.culture.active(), labels);
  });

  protected readonly format = computed(() => {
    const currency = this.series.value()?.currency ?? 'EUR';
    const culture = this.culture.active();
    return (value: number) => formatPrice(value, currency, culture);
  });
}
