import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime, formatPrice } from '@tendero/shared-util';
import { AccountsService } from '../../data-access/accounts.service';
import { type DashboardPeriod, OrderingService } from '../../data-access/ordering.service';
import { formatShare, shareOf } from './figures';

/**
 * Agents at the till (P11-7): their share of orders and sales, how many carried a
 * mandate, the rehearsals kept apart, and what the shop refused them — by rule,
 * by agent, and the latest few. Two contexts, two endpoints: Ordering knows what
 * was sold, Accounts' audit knows what was refused, and this widget is where the
 * two meet, rather than either context reaching into the other.
 */
@Component({
  selector: 'backoffice-agents-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './agents-widget.html',
  styleUrl: './agents-widget.css',
})
export class AgentsWidget {
  private readonly ordering = inject(OrderingService);
  private readonly accounts = inject(AccountsService);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly sales = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.ordering.agentSales(params.period),
  });

  protected readonly denials = rxResource({
    params: () => ({ period: this.period() }),
    stream: ({ params }) => this.accounts.agentDenials(params.period),
  });

  protected share(part: number, whole: number): string {
    return formatShare(shareOf(part, whole), this.culture.active());
  }

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  protected when(at: string): string {
    return formatDateTime(at, this.culture.active());
  }
}
