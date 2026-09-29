import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';

/**
 * What sold most (P11-7, a widget of its own since R-9): the five best sellers
 * with their units and revenue, as Ordering ranked them. A list, not a chart —
 * a name is read, not seen.
 */
@Component({
  selector: 'backoffice-best-sellers-widget',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './best-sellers-widget.html',
  styleUrl: './best-sellers-widget.css',
})
export class BestSellersWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly sales = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.sales(params.period, params.at),
  });

  protected money(amount: number): string {
    return formatPrice(amount, this.sales.value()?.currency ?? 'EUR', this.culture.active());
  }
}
