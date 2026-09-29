import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { DashboardPeriod } from '../../data-access/ordering.service';
import { DashboardRefresh } from './dashboard-refresh';
import { DashboardSources } from './dashboard-sources';
import { formatShare } from './figures';

/**
 * The shop's health against its targets (spec, Health — Seller Central's
 * Account Health): the cancellation rate with the standing the server decided,
 * said in a word as well as a colour. No chart: a figure against its target is
 * the reading. The defect and late-shipment rates join it with Plan 3's data.
 */
@Component({
  selector: 'backoffice-health-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './health-widget.html',
  styleUrl: './health-widget.css',
})
export class HealthWidget {
  private readonly sources = inject(DashboardSources);
  private readonly refresh = inject(DashboardRefresh);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected readonly operations = rxResource({
    params: () => ({ period: this.period(), at: this.refresh.at() }),
    stream: ({ params }) => this.sources.operations(params.period, params.at),
  });

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }
}
