import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
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
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claim-pane.html',
  styleUrl: './claim-pane.css',
})
export class ClaimPane {
  protected readonly store = inject(ClaimsReviewStore);

  /** The person closed the pane. */
  readonly closed = output<void>();

  protected readonly statementIn = statementIn;
  protected readonly percent = percent;
}
