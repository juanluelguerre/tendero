import { PricePipe } from '@tendero/shared-ui';
import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { BestSellersStore } from './best-sellers.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** What sold most (P11-7, a widget of its own since R-9): a list, not a chart — a name is read, not seen. */
@Component({
  selector: 'backoffice-best-sellers-widget',
  imports: [TranslocoDirective, WidgetFrame, PricePipe],
  templateUrl: './best-sellers-widget.html',
  host: { class: 'block' },
  providers: [BestSellersStore],
})
export class BestSellersWidget {
  protected readonly store = inject(BestSellersStore);
  protected readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
