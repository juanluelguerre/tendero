import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatShare } from '../figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { HealthStore } from './health.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** The shop's health against its targets (spec, Health): a figure against its target is the reading, said in a word as well as a colour. */
@Component({
  selector: 'backoffice-health-widget',
  imports: [TranslocoDirective, RouterLink, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './health-widget.html',
  styleUrl: './health-widget.css',
  providers: [HealthStore],
})
export class HealthWidget {
  protected readonly store = inject(HealthStore);
  private readonly culture = inject(CultureStore);

  protected share(rate: number): string {
    return formatShare(rate, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
