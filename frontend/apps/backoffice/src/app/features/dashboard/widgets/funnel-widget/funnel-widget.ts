import { Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatShare } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { FunnelStore } from './funnel.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** How the checkout converts (P11-7): carts started, checked out, abandoned and still open. Visits live in the observability stack, which the dashboard does not depend on. */
@Component({
  selector: 'backoffice-funnel-widget',
  imports: [TranslocoDirective, WidgetFrame],
  templateUrl: './funnel-widget.html',
  host: { class: 'block' },
  providers: [FunnelStore],
})
export class FunnelWidget {
  protected readonly store = inject(FunnelStore);
  private readonly culture = inject(CultureStore);

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
