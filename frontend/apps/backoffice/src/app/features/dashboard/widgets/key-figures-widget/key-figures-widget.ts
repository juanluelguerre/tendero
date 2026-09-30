import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { Sparkline } from '../../../../charts/sparkline';
import { type Change, formatChange, formatShare } from '../figures';
import { keyFigures } from './key-figures';
import type { DashboardPeriod } from '../../../../data-access/ordering.service';
import { KeyFiguresStore } from './key-figures.store';

/** The figures a shopkeeper asks first (spec, the Overview): each with how it moved against the period before and its trend where the server has a series. */
@Component({
  selector: 'backoffice-key-figures-widget',
  imports: [TranslocoDirective, Sparkline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './key-figures-widget.html',
  styleUrl: './key-figures-widget.css',
  providers: [KeyFiguresStore],
})
export class KeyFiguresWidget {
  protected readonly store = inject(KeyFiguresStore);
  private readonly culture = inject(CultureStore);

  protected readonly figures = computed(() => {
    const culture = this.culture.active();
    const currency = this.store.sales.value()?.currency ?? 'EUR';
    return keyFigures(this.store.sources(), {
      money: (amount) => formatPrice(amount, currency, culture),
      share: (ratio) => formatShare(ratio, culture),
      decimal: (value) => new Intl.NumberFormat(culture, { maximumFractionDigits: 2 }).format(value),
    });
  });

  protected changeText(change: Change): string | null {
    return formatChange(change, this.culture.active());
  }

  readonly period = input.required<DashboardPeriod>();

  constructor() {
    this.store.followPeriod(this.period);
  }
}
