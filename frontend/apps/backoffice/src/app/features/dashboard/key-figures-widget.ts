import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { Sparkline } from '../../charts/sparkline';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { type Change, formatChange, formatShare } from './figures';
import { keyFigures } from './key-figures';

/**
 * The figures a shopkeeper asks first (spec, the Overview's first row): revenue,
 * orders, units, the average order, conversion and the return rate, each with
 * how it moved against the period before and its trend where the server has a
 * series. Four endpoints, each arriving on its own; a figure shows when its
 * source does.
 */
@Component({
  selector: 'backoffice-key-figures-widget',
  imports: [TranslocoDirective, Sparkline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './key-figures-widget.html',
  styleUrl: './key-figures-widget.css',
})
export class KeyFiguresWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  private readonly params = () => ({ period: this.period(), at: this.refresh.at() });

  protected readonly sales = rxResource({ params: this.params, stream: ({ params }) => this.sources.sales(params.period, params.at) });
  private readonly series = rxResource({ params: this.params, stream: ({ params }) => this.sources.series(params.period, params.at) });
  private readonly funnel = rxResource({ params: this.params, stream: ({ params }) => this.sources.funnel(params.period, params.at) });
  private readonly operations = rxResource({ params: this.params, stream: ({ params }) => this.sources.operations(params.period, params.at) });

  protected readonly figures = computed(() => {
    const culture = this.culture.active();
    const currency = this.sales.value()?.currency ?? 'EUR';
    // A source that failed simply adds no figure: `value()` is undefined, never thrown.
    const value = <T>(resource: { hasValue(): boolean; value(): T }) => (resource.hasValue() ? resource.value() : undefined);
    return keyFigures(
      { sales: value(this.sales), series: value(this.series), funnel: value(this.funnel), operations: value(this.operations) },
      { money: (amount) => formatPrice(amount, currency, culture), share: (ratio) => formatShare(ratio, culture) },
    );
  });

  protected changeText(change: Change): string | null {
    return formatChange(change, this.culture.active());
  }
}
