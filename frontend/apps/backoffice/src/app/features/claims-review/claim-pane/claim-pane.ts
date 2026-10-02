import { Component, inject, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ClaimForReview } from '@tendero/shared-api';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { percent, statementIn } from '../claim-statement';
import { ClaimSupport } from '../claim-support/claim-support';
import { ClaimsReviewStore } from '../claims-review.store';

/**
 * The open claim beside the queue: its sentence in both languages, the quotes
 * that support it with their scores, who proposed it — AI proposes, a person
 * approves (invariant 12) — and Approve, the view's one clay action.
 */
@Component({
  selector: 'backoffice-claim-pane',
  imports: [TranslocoDirective, WorklistPane, ClaimSupport],
  templateUrl: './claim-pane.html',
  // The pane is laid out by the page's list-and-pane grid, as if it were its child.
  host: { class: 'contents' },
})
export class ClaimPane {
  protected readonly store = inject(ClaimsReviewStore);

  /** The person closed the pane. */
  readonly closed = output<void>();

  /** The person wants to review this product's claims together (S-5). */
  readonly reviewAll = output<ClaimForReview>();

  protected readonly statementIn = statementIn;
  protected readonly percent = percent;
}
