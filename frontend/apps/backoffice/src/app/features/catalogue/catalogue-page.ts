import { Component, inject } from '@angular/core';
import { CatalogueList } from './catalogue-list/catalogue-list';
import { CataloguePane } from './catalogue-pane/catalogue-pane';
import { CatalogueStore } from './catalogue.store';

/**
 * Every product, by status, and what can be done with one (the redesign's
 * R-4.3): published from here or taken back to review, with its variants one
 * link away. It opens on what is on sale.
 *
 * **Withdrawing is undoable, so it does not ask**: it offers Undo, which is
 * publishing again — the domain's own reverse, audited like any other move
 * (R-2, P12). Importing lives here now, as a quiet action: it is the
 * catalogue's, and it was the review queue's clay button whenever that queue
 * was empty (R-1, B-F7).
 */
@Component({
  selector: 'backoffice-catalogue-page',
  imports: [CatalogueList, CataloguePane],
  templateUrl: './catalogue-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list and the pane have of their own is in their folders.
  host: { class: 'block' },
  providers: [CatalogueStore],
})
export class CataloguePage {
  protected readonly store = inject(CatalogueStore);
}
