import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { FilterChips } from '../../worklist/filter-chips/filter-chips';
import { Find } from '../../worklist/find/find';
import { ListState } from '../../worklist/list-state/list-state';
import { StockHolds } from './stock-holds/stock-holds';
import { StockShelves } from './stock-shelves/stock-shelves';
import { StockStore } from './stock.store';

/**
 * What is on every shelf, and what is being held.
 *
 * The first screen in the backoffice that WRITES, and the shape of it is the
 * argument: a count is not an increment. You type what the shelf actually holds
 * and the difference is the system's to work out — which is why pressing save
 * twice changes nothing the second time, and why a stocktake and a delivery are
 * different words in the domain.
 *
 * The reservations panel sits beside the grid rather than on its own route
 * because it answers the question the grid provokes. "2 available" out of five
 * on hand is not information until you can see the three that are held, for
 * which order, and — when the row is `Released` — the sentence explaining why
 * inventory said no.
 *
 * Editing is per row and optimistic in one direction only: the response carries
 * the recomputed availability, so the row is replaced with what the server
 * actually stored rather than with what was typed. `available` is derived
 * server-side and a grid that subtracted for itself would disagree with the
 * search index the moment a hold expired. The state is the page's store
 * (ADR 0047); the shelves and the holds are its two halves.
 */
@Component({
  selector: 'backoffice-stock',
  imports: [TranslocoDirective, FilterChips, StockShelves, StockHolds, Find, ListState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stock-page.html',
  styleUrl: './stock-page.css',
  providers: [StockStore],
})
export class StockPage {
  protected readonly store = inject(StockStore);
}
