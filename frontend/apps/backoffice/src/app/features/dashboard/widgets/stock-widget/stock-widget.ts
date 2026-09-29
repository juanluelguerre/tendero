import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { StockStore } from './stock.store';

/** The stock (spec, Operations): what to restock and what is sold out, and from M the lowest first. Stock is now, not a window: no period. */
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
}
