import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime } from '@tendero/shared-util';
import { BarChart } from '../../charts/bar-chart';
import type { BarChartData } from '../../charts/chart-contract';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';

/**
 * What the shop refused agents (P11-7, a widget of its own since R-9): how many,
 * by rule — ranked in rows from M — and the latest few with their agent. Accounts'
 * audit knows what was refused; Ordering never sees it, which is why this is not
 * one widget with what agents bought.
 */
@Component({
  selector: 'backoffice-refusals-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './refusals-widget.html',
  styleUrl: './refusals-widget.css',
})
export class RefusalsWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);
  /** The rules' words as a signal, so the rows follow a change of language. */
  private readonly rules = translateObjectSignal('dashboard.refusals.rule');
  private readonly words = translateObjectSignal('dashboard.refusals');

  readonly period = input.required<DashboardPeriod>();

  protected readonly denials = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.agentDenials(params.period, params.at),
  });

  protected readonly chart = computed<BarChartData | null>(() => {
    const denials = this.denials.value();
    if (!denials || denials.byReason.length === 0) return null;
    const rules = this.rules() as Partial<Record<string, string>>;
    const words = this.words() as Partial<Record<string, string>>;
    return {
      categories: denials.byReason.map((row) => rules[row.reason] ?? rules['denied'] ?? row.reason),
      series: [{ key: 'refused', label: words['denied'] ?? '', role: 'data', values: denials.byReason.map((row) => row.count) }],
    };
  });

  protected readonly count = (value: number) => String(value);

  protected when(at: string): string {
    return formatDateTime(at, this.culture.active());
  }
}
