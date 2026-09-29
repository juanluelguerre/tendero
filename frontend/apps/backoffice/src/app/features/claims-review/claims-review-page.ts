import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BackofficeUsage } from '../../usage';
import { landing, stepOf } from '../../worklist/list-keys';
import { ClaimList } from './claim-list/claim-list';
import { ClaimPane } from './claim-pane/claim-pane';
import { ClaimsReviewStore } from './claims-review.store';

/**
 * Claims a model proposed and a person decides (phase 10, ADR 0018), as R-3
 * drew the lists of work (R-4.3): the queue opens on the weakly supported —
 * the ones a person is for — keeps both decisions on the row, and opens a
 * claim beside the list with the statement in both languages, every quote that
 * backs it and how well, and who proposed it.
 *
 * **No bulk approve, on purpose** (R-2, P4): each claim is a judgement about one
 * product, and a bulk approve is how a review queue becomes a rubber stamp.
 * Nothing reaches the catalogue until a person approves it (invariant 12).
 */
@Component({
  selector: 'backoffice-claims-review-page',
  imports: [ClaimList, ClaimPane],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claims-review-page.html',
  styleUrl: './claims-review-page.css',
  host: { '(document:keydown)': 'key($event)' },
  providers: [ClaimsReviewStore],
})
export class ClaimsReviewPage {
  protected readonly store = inject(ClaimsReviewStore);
  private readonly usage = inject(BackofficeUsage);

  /** j and k move down and up the claims, the pane following (the redesign's R-3, P5). */
  protected key(event: KeyboardEvent): void {
    const step = stepOf(event, !!document.querySelector('dialog[open]'));
    if (!step) return;
    const claims = this.store.claims();
    const index = landing(claims.findIndex((claim) => claim.claimId === this.store.selectedId()), claims.length, step);
    if (index === null) return;
    event.preventDefault();
    this.usage.affordance('keys', step === 1 ? 'next' : 'previous');
    this.store.open(claims[index]);
  }
}
