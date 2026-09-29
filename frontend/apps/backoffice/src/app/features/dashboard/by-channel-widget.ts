import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { BarChart } from '../../charts/bar-chart';
import type { BarChartData } from '../../charts/chart-contract';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { refreshedResource } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { formatShare, shareOf } from './figures';

/**
 * Who bought (spec, By channel): the period's orders shared between people and
 * agents — two parts of one whole, so one bar, agents in clay. The surface an
 * agent came through (MCP or UCP) arrives with Plan 3's data.
 */
@Component({
  selector: 'backoffice-by-channel-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './by-channel-widget.html',
  styleUrl: './by-channel-widget.css',
})
export class ByChannelWidget {
  private readonly sources = inject(DashboardSources);
  private readonly culture = inject(CultureStore);
  /** A signal of the words, so the legend follows a change of language. */
  private readonly words = translateObjectSignal('dashboard.byChannel');

  readonly period = input.required<DashboardPeriod>();

  protected readonly sales = refreshedResource({
    params: () => ({ period: this.period() }),
    stream: (params, at) => this.sources.agentSales(params.period, at),
  });

  protected readonly chart = computed<BarChartData | null>(() => {
    const sales = this.sales.value();
    if (!sales || sales.orders === 0) return null;
    const words = this.words() as Partial<Record<'people' | 'agents' | 'orders', string>>;
    return {
      categories: [words.orders ?? ''],
      series: [
        { key: 'people', label: words.people ?? '', role: 'data', values: [sales.orders - sales.agentOrders] },
        { key: 'agents', label: words.agents ?? '', role: 'agent', values: [sales.agentOrders] },
      ],
    };
  });

  protected readonly agentShare = computed(() => {
    const sales = this.sales.value();
    return sales ? formatShare(shareOf(sales.agentOrders, sales.orders), this.culture.active()) : '';
  });

  protected readonly revenueShare = computed(() => {
    const sales = this.sales.value();
    return sales ? formatShare(shareOf(sales.agentRevenue, sales.revenue), this.culture.active()) : '';
  });

  protected readonly count = (value: number) => String(value);
}
