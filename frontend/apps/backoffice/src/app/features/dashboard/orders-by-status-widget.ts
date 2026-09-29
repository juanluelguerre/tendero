import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { BarChart } from '../../charts/bar-chart';
import type { BarChartData } from '../../charts/chart-contract';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';

/**
 * The work in hand (P11-7, a widget of its own since R-9): orders to ship and the
 * late ones, being confirmed, payment failed, cancelled — each label the way into
 * its filtered list, and from M the same counts as rows.
 */
@Component({
  selector: 'backoffice-orders-by-status-widget',
  imports: [TranslocoDirective, RouterLink, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orders-by-status-widget.html',
  styleUrl: './orders-by-status-widget.css',
})
export class OrdersByStatusWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  /** A signal of the words, so the rows' labels follow a change of language. */
  private readonly words = translateObjectSignal('dashboard.ordersByStatus');

  readonly period = input.required<DashboardPeriod>();

  protected readonly operations = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.operations(params.period, params.at),
  });

  protected readonly chart = computed<BarChartData | null>(() => {
    const ops = this.operations.value();
    if (!ops) return null;
    const words = this.words() as Partial<Record<string, string>>;
    return {
      categories: [words['toShip'] ?? '', words['inProgress'] ?? '', words['paymentFailed'] ?? '', words['cancelledCount'] ?? ''],
      series: [{ key: 'orders', label: words['title'] ?? '', role: 'data', values: [ops.toShip, ops.inProgress, ops.paymentFailed, ops.cancelled] }],
    };
  });

  protected readonly count = (value: number) => String(value);
}
