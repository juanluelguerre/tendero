import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { formatShare } from '../figures';
import { SellThroughStore } from './sell-through.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/**
 * Sell-through (spec, Sales; R-9, D-7): what moves and what sits in the period,
 * sold over sold and left — two short lists, read rather than seen.
 */
@Component({
  selector: 'backoffice-sell-through-widget',
  imports: [TranslocoDirective, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sell-through-widget.html',
  host: { class: 'block' },
  providers: [SellThroughStore],
})
export class SellThroughWidget {
  protected readonly store = inject(SellThroughStore);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }

  constructor() {
    this.store.followPeriod(this.period);
  }
}
