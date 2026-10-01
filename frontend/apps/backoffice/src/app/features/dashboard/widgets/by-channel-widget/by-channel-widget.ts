import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { BarChart } from '../../../../charts/bar-chart';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { formatShare } from '../figures';
import { ByChannelStore } from './by-channel.store';
import { WidgetFrame } from '../widget-frame/widget-frame';

/** Who bought (spec, By channel): people against agents, presented from its store (ADR 0047). */
@Component({
  selector: 'backoffice-by-channel-widget',
  imports: [TranslocoDirective, BarChart, WidgetFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './by-channel-widget.html',
  host: { class: 'block' },
  providers: [ByChannelStore],
})
export class ByChannelWidget {
  protected readonly store = inject(ByChannelStore);
  private readonly culture = inject(CultureStore);

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }

  protected share(ratio: number): string {
    return formatShare(ratio, this.culture.active());
  }

  protected readonly count = (value: number) => String(value);
}
