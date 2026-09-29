import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { refreshedResource } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { formatShare, shareOf } from './figures';

/**
 * What agents buy (P11-7, a widget of its own since R-9): their share of orders
 * and sales, how many carried a mandate, the rehearsals kept apart, and the
 * agents that bought most. Ordering's half of the agents' story; what the shop
 * refused them is Accounts' half, in the widget beside it.
 */
@Component({
  selector: 'backoffice-agent-sales-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-sales-widget.html',
  styleUrl: './agent-sales-widget.css',
})
export class AgentSalesWidget {
  private readonly sources = inject(DashboardSources);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly sales = refreshedResource({
    params: () => ({ period: this.period() }),
    stream: (params, at) => this.sources.agentSales(params.period, at),
  });

  protected share(part: number, whole: number): string {
    return formatShare(shareOf(part, whole), this.culture.active());
  }

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }
}
