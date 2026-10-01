import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, valueOf } from '@tendero/shared-util';
import { BarChart } from '../../../../charts/bar-chart';
import { formatShare, shareOf } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { AgentSalesStore } from './agent-sales.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** What agents buy (P11-7, a widget of its own since R-9): their share of orders and sales, mandates, rehearsals and the agents that bought most. */
@Component({
  selector: 'backoffice-agent-sales-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame],
  templateUrl: './agent-sales-widget.html',
  host: { class: 'block' },
  providers: [AgentSalesStore],
})
export class AgentSalesWidget {
  protected readonly store = inject(AgentSalesStore);
  private readonly culture = inject(CultureStore);

  /** What agents bought each day, in the reader's money. */
  protected readonly amount = (value: number): string =>
    formatPrice(value, valueOf(this.store.sales)?.currency ?? 'EUR', this.culture.active());

  protected share(part: number, whole: number): string {
    return formatShare(shareOf(part, whole), this.culture.active());
  }

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
