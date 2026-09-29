import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { BarChart } from '../../charts/bar-chart';
import type { BarChartData } from '../../charts/chart-contract';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';

/**
 * Who bought (spec, Growth's Customers): the buyers who were new against those
 * who came back — two parts of one whole, so one bar, the returning ones in the
 * muted colour of the base and the new ones in the data colour of the change.
 * The same figures by day arrive with Plan 3's series.
 */
@Component({
  selector: 'backoffice-customers-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customers-widget.html',
  styleUrl: './customers-widget.css',
})
export class CustomersWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  /** A signal of the words, so the legend follows a change of language. */
  private readonly words = translateObjectSignal('dashboard.customers');

  readonly period = input.required<DashboardPeriod>();

  protected readonly funnel = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.funnel(params.period, params.at),
  });

  protected readonly chart = computed<BarChartData | null>(() => {
    const funnel = this.funnel.value();
    if (!funnel || funnel.newCustomers + funnel.returningCustomers === 0) return null;
    const words = this.words() as Partial<Record<string, string>>;
    return {
      categories: [words['buyers'] ?? ''],
      series: [
        { key: 'new', label: words['newCustomers'] ?? '', role: 'data', values: [funnel.newCustomers] },
        { key: 'returning', label: words['returningCustomers'] ?? '', role: 'previous', values: [funnel.returningCustomers] },
      ],
    };
  });

  protected readonly count = (value: number) => String(value);
}
