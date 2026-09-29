import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { formatShare, shareOf } from './figures';

/**
 * How the checkout converts (P11-7): carts started, checked out, abandoned and
 * still open. Visits are not here — they live in the observability stack, which
 * the dashboard does not depend on — so the top of this funnel is a cart. The
 * buyers, new and returning, are the Growth tab's customers since R-9.
 */
@Component({
  selector: 'backoffice-funnel-widget',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './funnel-widget.html',
  styleUrl: './funnel-widget.css',
})
export class FunnelWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly funnel = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.funnel(params.period, params.at),
  });

  /** The steps as bars, each as wide as its share of the carts started. */
  protected readonly steps = computed(() => {
    const funnel = this.funnel.value();
    if (!funnel) return [];
    return [
      { key: 'started', count: funnel.cartsStarted, share: funnel.cartsStarted === 0 ? 0 : 1 },
      { key: 'checkedOut', count: funnel.checkedOut, share: shareOf(funnel.checkedOut, funnel.cartsStarted) },
      { key: 'abandoned', count: funnel.abandoned, share: shareOf(funnel.abandoned, funnel.cartsStarted) },
      { key: 'stillOpen', count: funnel.stillOpen, share: shareOf(funnel.stillOpen, funnel.cartsStarted) },
    ];
  });

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }
}
