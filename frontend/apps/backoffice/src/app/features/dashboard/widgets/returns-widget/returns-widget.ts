import { PricePipe } from '@tendero/shared-ui';
import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { BarChart } from '../../../../charts/bar-chart';
import { formatShare } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { ReturnsStore } from './returns.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** Returns (spec, Operations): open, the return rate, what was refunded, and from M the open ones ranked by reason. */
@Component({
  selector: 'backoffice-returns-widget',
  imports: [TranslocoDirective, RouterLink, BarChart, WidgetFrame, PricePipe],
  templateUrl: './returns-widget.html',
  host: { class: 'block' },
  providers: [ReturnsStore],
})
export class ReturnsWidget {
  protected readonly store = inject(ReturnsStore);
  protected readonly culture = inject(CultureStore);

  protected readonly count = (value: number) => String(value);

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
