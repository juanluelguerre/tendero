import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { BarChart } from '../../../../charts/bar-chart';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { OrdersByStatusStore } from './orders-by-status.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** The work in hand (P11-7, a widget of its own since R-9): each label the way into its filtered list, and from M the same counts as rows. */
@Component({
  selector: 'backoffice-orders-by-status-widget',
  imports: [TranslocoDirective, RouterLink, BarChart, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './orders-by-status-widget.html',
  host: { class: 'block' },
  providers: [OrdersByStatusStore],
})
export class OrdersByStatusWidget {
  protected readonly store = inject(OrdersByStatusStore);
  protected readonly count = (value: number) => String(value);

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
