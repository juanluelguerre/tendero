import { formatMajor, majorUnits, ShopCurrency } from '@tendero/shared-util';
import { PricePipe } from '@tendero/shared-ui';
import { Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { BarChart } from '../../../../charts/bar-chart';
import type { BarChartData } from '../../../../charts/chart-contract';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { ByCategoryStore } from './by-category.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/**
 * Sales by category (spec, Sales; R-9, D-7): the categories the period's lines
 * were sold in, the most revenue first — rows at M, the list with units at S.
 */
@Component({
  selector: 'backoffice-by-category-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame, PricePipe],
  templateUrl: './by-category-widget.html',
  host: { class: 'block' },
  providers: [ByCategoryStore],
})
export class ByCategoryWidget {
  protected readonly store = inject(ByCategoryStore);
  protected readonly culture = inject(CultureStore);

  /** The chart's axis and figures: a function the chart calls, where a template would use the price pipe. */
  protected readonly money = (amount: number): string =>
    formatMajor(amount, this.store.mix.value()?.unrecorded.revenue.currency ?? ShopCurrency, this.culture.active());

  readonly period = input.required<DashboardPeriod>();

  protected readonly chart = computed<BarChartData | null>(() => {
    const rows = this.store.rows();
    return rows.length === 0
      ? null
      : {
          categories: rows.map((row) => row.label),
          series: [{ key: 'revenue', label: '', role: 'data', values: rows.map((row) => majorUnits(row.revenue)) }],
        };
  });

  constructor() {
    this.store.followPeriod(this.period);
  }
}
