import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductReview } from '../../../catalogue/product-review/product-review';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { CatalogueStore } from '../catalogue.store';
import { ProductStatusTags } from '../product-status-tags/product-status-tags';

/**
 * The open product beside the list: as a shopper would see it, and the one
 * change its status allows — publish a draft, or withdraw what is on sale,
 * which does not ask because it offers Undo.
 */
@Component({
  selector: 'backoffice-catalogue-pane',
  imports: [RouterLink, TranslocoDirective, WorklistPane, ProductReview, ProductStatusTags],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalogue-pane.html',
  // The pane is laid out by the page's list-and-pane grid, as if it were its child.
  host: { class: 'contents' },
})
export class CataloguePane {
  protected readonly store = inject(CatalogueStore);

  /** The person closed the pane. */
  readonly closed = output<void>();
}
