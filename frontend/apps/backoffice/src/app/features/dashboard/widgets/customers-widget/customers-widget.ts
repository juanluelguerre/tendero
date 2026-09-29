import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { BarChart } from '../../../../charts/bar-chart';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { CustomersStore } from './customers.store';

/** Who bought (spec, Growth's Customers): new against returning, as figures and as one share. */
@Component({
  selector: 'backoffice-customers-widget',
  imports: [TranslocoDirective, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './customers-widget.html',
  styleUrl: './customers-widget.css',
  providers: [CustomersStore],
})
export class CustomersWidget {
  protected readonly store = inject(CustomersStore);
  protected readonly count = (value: number) => String(value);

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
