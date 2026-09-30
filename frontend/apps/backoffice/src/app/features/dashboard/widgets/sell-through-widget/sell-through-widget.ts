import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { formatShare } from '../figures';
import { SellThroughStore } from './sell-through.store';

/**
 * Sell-through (spec, Sales; R-9, D-7): what moves and what sits in the period,
 * sold over sold and left — two short lists, read rather than seen.
 */
@Component({
  selector: 'backoffice-sell-through-widget',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sell-through-widget.html',
  styleUrl: './sell-through-widget.css',
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
