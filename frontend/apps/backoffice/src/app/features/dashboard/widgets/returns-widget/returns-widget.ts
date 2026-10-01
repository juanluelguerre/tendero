import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../../../charts/bar-chart';
import { formatShare } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { ReturnsStore } from './returns.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** Returns (spec, Operations): open, the return rate, what was refunded, and from M the open ones ranked by reason. */
@Component({
  selector: 'backoffice-returns-widget',
  imports: [TranslocoDirective, RouterLink, BarChart, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './returns-widget.html',
  styleUrl: './returns-widget.css',
  providers: [ReturnsStore],
})
export class ReturnsWidget {
  protected readonly store = inject(ReturnsStore);
  private readonly culture = inject(CultureStore);

  protected readonly count = (value: number) => String(value);

  protected money(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
