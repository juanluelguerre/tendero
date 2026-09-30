import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { StockStore } from './stock.store';

/**
 * The stock (spec, Operations): what to restock and what is sold out, and from M
 * what runs out in fewer than fourteen days at the shop's pace, what did not
 * sell in four weeks, and the closest to running out (R-9, D-7). Coverage is the
 * last four weeks' pace, not a window: no period.
 */
@Component({
  selector: 'backoffice-stock-widget',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-widget.html',
  styleUrl: './stock-widget.css',
  providers: [StockStore],
})
export class StockWidget {
  protected readonly store = inject(StockStore);
  private readonly culture = inject(CultureStore);

  /** Days of cover in the reader's culture: one decimal at most. */
  protected days(value: number): string {
    return new Intl.NumberFormat(this.culture.active(), { maximumFractionDigits: 1 }).format(value);
  }
}
