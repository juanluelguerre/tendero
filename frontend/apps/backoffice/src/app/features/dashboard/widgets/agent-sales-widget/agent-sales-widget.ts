import { PricePipe } from '@tendero/shared-ui';
import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatMajor, ShopCurrency } from '@tendero/shared-util';
import { BarChart } from '../../../../charts/bar-chart';
import { formatShare, shareOf } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { AgentSalesStore } from './agent-sales.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** What agents buy (P11-7, a widget of its own since R-9): their share of orders and sales, mandates, rehearsals and the agents that bought most. */
@Component({
  selector: 'backoffice-agent-sales-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame, PricePipe],
  templateUrl: './agent-sales-widget.html',
  host: { class: 'block' },
  providers: [AgentSalesStore],
})
export class AgentSalesWidget {
  protected readonly store = inject(AgentSalesStore);
  protected readonly culture = inject(CultureStore);

  /** What agents bought each day, in the reader's money: the chart's own numbers, in major units. */
  protected readonly amount = (value: number): string =>
    formatMajor(value, this.store.sales.value()?.revenue.currency ?? ShopCurrency, this.culture.active());

  protected share(part: number, whole: number): string {
    return formatShare(shareOf(part, whole), this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
