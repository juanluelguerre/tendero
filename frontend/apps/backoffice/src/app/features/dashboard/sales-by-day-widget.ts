import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../charts/bar-chart';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';
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
  private readonly ordering = inject(OrderingService);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);

  readonly period = input.required<DashboardPeriod>();

  protected readonly series = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.ordering.salesSeries(params.period),
  });

  protected readonly chart = computed(() => {
    const series = this.series.value();
    if (!series) return null;
    const labels = {
      current: this.transloco.translate(`dashboard.salesByDay.current.${series.period}`),
      previous: this.transloco.translate(`dashboard.salesByDay.previous.${series.period}`),
    };
    return toBarChart(series, this.culture.active(), labels);
  });

  protected readonly format = computed(() => {
    const currency = this.series.value()?.currency ?? 'EUR';
    const culture = this.culture.active();
    return (value: number) => formatPrice(value, currency, culture);
  });
}
