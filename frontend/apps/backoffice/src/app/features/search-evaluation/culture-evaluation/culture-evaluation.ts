import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { CultureView } from '../evaluation-view';

/**
 * One language's measure: lexical against hybrid on NDCG@10, recall@50 and
 * time, each against its threshold, and every query worst first. No state: it
 * says the view it is given, in the reader's number format.
 */
@Component({
  selector: 'backoffice-culture-evaluation',
  imports: [TranslocoDirective, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './culture-evaluation.html',
  host: { class: 'block' },
})
export class CultureEvaluation {
  private readonly culture = inject(CultureStore);

  readonly view = input.required<CultureView>();

  private readonly decimals = computed(() => new Intl.NumberFormat(this.culture.active(), { minimumFractionDigits: 3, maximumFractionDigits: 3 }));
  private readonly signedDecimals = computed(
    () => new Intl.NumberFormat(this.culture.active(), { minimumFractionDigits: 3, maximumFractionDigits: 3, signDisplay: 'exceptZero' }),
  );
  private readonly wholeNumbers = computed(() => new Intl.NumberFormat(this.culture.active(), { maximumFractionDigits: 0 }));

  protected score(value: number | null): string {
    return value === null ? '—' : this.decimals().format(value);
  }

  protected difference(value: number | null): string {
    return value === null ? '—' : this.signedDecimals().format(value);
  }

  protected milliseconds(value: number): string {
    return this.wholeNumbers().format(value);
  }
}
