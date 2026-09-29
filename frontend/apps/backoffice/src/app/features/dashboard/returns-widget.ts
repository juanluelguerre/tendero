import type { ReturnReason } from '@tendero/shared-api';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../charts/bar-chart';
import type { BarChartData } from '../../charts/chart-contract';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { refreshedResource } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { formatShare } from './figures';

/**
 * Returns (spec, Operations): open, the return rate and what was refunded, and
 * from M the open ones ranked by reason in rows — in the returns screen's own
 * words, so five reasons have one translation, not two that drift.
 */
@Component({
  selector: 'backoffice-returns-widget',
  imports: [TranslocoDirective, RouterLink, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './returns-widget.html',
  styleUrl: './returns-widget.css',
})
export class ReturnsWidget {
  private readonly sources = inject(DashboardSources);
  private readonly culture = inject(CultureStore);
  /** The returns screen's words for a reason, as a signal so the rows follow a change of language. */
  private readonly reasons = translateObjectSignal('returns.reason');
  private readonly words = translateObjectSignal('dashboard.returns');

  readonly period = input.required<DashboardPeriod>();

  protected readonly operations = refreshedResource({
    params: () => ({ period: this.period() }),
    stream: (params, at) => this.sources.operations(params.period, at),
  });

  protected readonly chart = computed<BarChartData | null>(() => {
    const ops = this.operations.value();
    if (!ops || ops.openReturnsByReason.length === 0) return null;
    const reasons = this.reasons() as Partial<Record<ReturnReason, string>>;
    const words = this.words() as Partial<Record<string, string>>;
    return {
      categories: ops.openReturnsByReason.map((row) => reasons[row.reason] ?? row.reason),
      series: [{ key: 'open', label: words['openReturns'] ?? '', role: 'data', values: ops.openReturnsByReason.map((row) => row.count) }],
    };
  });

  protected readonly count = (value: number) => String(value);

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }
}
