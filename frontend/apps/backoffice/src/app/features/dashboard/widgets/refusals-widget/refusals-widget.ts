import { DateTimePipe } from '@tendero/shared-ui';
import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { BarChart } from '../../../../charts/bar-chart';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { RefusalsStore } from './refusals.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** What the shop refused agents (P11-7, a widget of its own since R-9): how many, by rule, and the latest few with their agent. */
@Component({
  selector: 'backoffice-refusals-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame, DateTimePipe],
  templateUrl: './refusals-widget.html',
  host: { class: 'block' },
  providers: [RefusalsStore],
})
export class RefusalsWidget {
  protected readonly store = inject(RefusalsStore);
  protected readonly culture = inject(CultureStore);

  protected readonly count = (value: number) => String(value);

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
