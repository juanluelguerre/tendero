import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductReview } from '../../../catalogue/product-review/product-review';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { ReviewQueueStore } from '../review-queue.store';

/**
 * The open draft beside the queue: the product as a shopper would see it, in
 * every language, and the view's one clay action — publish. It reads and acts
 * through the page's store, which the list shares.
 */
@Component({
  selector: 'backoffice-review-pane',
  imports: [RouterLink, TranslocoDirective, WorklistPane, ProductReview],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './review-pane.html',
  styleUrl: './review-pane.css',
})
export class ReviewPane {
  protected readonly store = inject(ReviewQueueStore);

  /** The person closed the pane. */
  readonly closed = output<void>();
}
