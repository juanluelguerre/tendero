import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Pager } from '../../../worklist/pager/pager';
import { PageSize, shelfOf, StockStore } from '../stock.store';

/**
 * The shelves: each SKU in each warehouse, what it holds, what is spoken for and
 * what is left, and the one editable cell on the screen — a count replaces the
 * number; it does not add to it. The list and its pager are one block.
 */
@Component({
  selector: 'backoffice-stock-shelves',
  imports: [TranslocoDirective, Pager],
  templateUrl: './stock-shelves.html',
  host: { class: 'block min-w-0' },
})
export class StockShelves {
  protected readonly store = inject(StockStore);
  protected readonly pageSize = PageSize;
  protected readonly shelfOf = shelfOf;
}
