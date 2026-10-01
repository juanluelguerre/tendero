import { Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
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
  imports: [TranslocoDirective, BarChart, WidgetFrame],
  templateUrl: './by-category-widget.html',
  host: { class: 'block' },
  providers: [ByCategoryStore],
})
export class ByCategoryWidget {
  protected readonly store = inject(ByCategoryStore);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly money = (amount: number): string =>
    formatPrice(amount, this.store.mix.value()?.currency ?? 'EUR', this.culture.active());

  protected readonly chart = computed<BarChartData | null>(() => {
    const rows = this.store.rows();
    return rows.length === 0
      ? null
      : {
          categories: rows.map((row) => row.label),
          series: [{ key: 'revenue', label: '', role: 'data', values: rows.map((row) => row.revenue) }],
        };
  });

  constructor() {
    this.store.followPeriod(this.period);
  }
}
