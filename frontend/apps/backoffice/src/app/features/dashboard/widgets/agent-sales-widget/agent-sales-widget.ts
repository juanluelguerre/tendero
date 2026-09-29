import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { formatShare, shareOf } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { AgentSalesStore } from './agent-sales.store';

/** What agents buy (P11-7, a widget of its own since R-9): their share of orders and sales, mandates, rehearsals and the agents that bought most. */
@Component({
  selector: 'backoffice-agent-sales-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agent-sales-widget.html',
  styleUrl: './agent-sales-widget.css',
  providers: [AgentSalesStore],
})
export class AgentSalesWidget {
  protected readonly store = inject(AgentSalesStore);
  private readonly culture = inject(CultureStore);

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
