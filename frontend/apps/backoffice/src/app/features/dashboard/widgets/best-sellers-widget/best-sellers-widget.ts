import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, valueOf } from '@tendero/shared-util';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { BestSellersStore } from './best-sellers.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** What sold most (P11-7, a widget of its own since R-9): a list, not a chart — a name is read, not seen. */
@Component({
  selector: 'backoffice-best-sellers-widget',
  imports: [TranslocoDirective, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './best-sellers-widget.html',
  styleUrl: './best-sellers-widget.css',
  providers: [BestSellersStore],
})
export class BestSellersWidget {
  protected readonly store = inject(BestSellersStore);
  private readonly culture = inject(CultureStore);

  protected money(amount: number): string {
    return formatPrice(amount, valueOf(this.store.sales)?.currency ?? 'EUR', this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
