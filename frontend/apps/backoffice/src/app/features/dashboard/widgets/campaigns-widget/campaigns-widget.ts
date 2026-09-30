import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, translateObjectSignal } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { BarChart } from '../../../../charts/bar-chart';
import type { BarChartData } from '../../../../charts/chart-contract';
import { changeOf, formatChange } from '../figures';
import { CampaignsStore } from './campaigns.store';

/**
 * A campaign against its baseline (spec, Overview and Growth; R-9, D-7): what it
 * sold each day against the same weekday over the four weeks before it opened,
 * and what that adds up to — an estimate, and said to be one. Its period is its
 * own window, not the dashboard's.
 */
@Component({
  selector: 'backoffice-campaigns-widget',
  imports: [TranslocoDirective, RouterLink, BarChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './campaigns-widget.html',
  styleUrl: './campaigns-widget.css',
  providers: [CampaignsStore],
})
export class CampaignsWidget {
  protected readonly store = inject(CampaignsStore);
  private readonly culture = inject(CultureStore);
  private readonly words = translateObjectSignal('dashboard.campaigns');

  protected readonly money = (amount: number): string => formatPrice(amount, this.store.uplift.value()?.currency ?? 'EUR', this.culture.active());

  protected readonly name = computed(() => {
    const campaign = this.store.campaign();
    if (!campaign) return '';
    const names = campaign.name as Record<string, string>;
    return names[this.culture.active()] || names['en'] || campaign.code;
  });

  /** The uplift as a change against the baseline: +27 %, in the reader's culture. */
  protected readonly uplift = computed(() => {
    const figures = this.store.uplift.value();
    return figures?.baseline != null ? formatChange(changeOf(figures.sold, figures.baseline), this.culture.active()) : null;
  });

  /** The days that have ended, sold against their baseline: today, under way, would read as a fall. */
  protected readonly chart = computed<BarChartData | null>(() => {
    const figures = this.store.uplift.value();
    const days = figures?.days.filter((day) => !day.future && !day.partial) ?? [];
    if (!figures?.hasBaseline || days.length === 0) return null;
    // In the zone the server counted the days in (ADR 0046): never one assumed here.
    const format = new Intl.DateTimeFormat(this.culture.active(), { day: 'numeric', month: 'short', timeZone: figures.timeZone });
    const words = this.words() as Partial<Record<'sold' | 'baseline', string>>;
    return {
      categories: days.map((day) => format.format(new Date(day.start)).replace('.', '')),
      series: [
        { key: 'sold', label: words.sold ?? '', role: 'data', values: days.map((day) => day.sold) },
        { key: 'baseline', label: words.baseline ?? '', role: 'previous', values: days.map((day) => day.baseline ?? 0) },
      ],
    };
  });
}
