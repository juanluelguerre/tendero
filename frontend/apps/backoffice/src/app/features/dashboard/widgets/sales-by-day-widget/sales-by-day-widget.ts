import { Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatMajor, ShopCurrency } from '@tendero/shared-util';
import { BarChart } from '../../../../charts/bar-chart';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { SalesByDayStore } from './sales-by-day.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** Sales by hour or by day against the period before, on the shop's clock (R-9, ADR 0046). */
@Component({
  selector: 'backoffice-sales-by-day-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame],
  templateUrl: './sales-by-day-widget.html',
  host: { class: 'block' },
  providers: [SalesByDayStore],
})
export class SalesByDayWidget {
  protected readonly store = inject(SalesByDayStore);
  private readonly culture = inject(CultureStore);

  protected readonly format = computed(() => {
    const currency = this.store.series.value()?.current[0]?.revenue.currency ?? ShopCurrency;
    const culture = this.culture.active();
    return (value: number) => formatMajor(value, currency, culture);
  });

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
