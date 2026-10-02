import { Component, inject, viewChild } from '@angular/core';
import type { ClaimForReview } from '@tendero/shared-api';
import { BackofficeUsage } from '../../usage';
import { landing, stepOf } from '../../worklist/list-keys';
import { ClaimList } from './claim-list/claim-list';
import { ClaimPane } from './claim-pane/claim-pane';
import { ClaimsBulkReview } from './claims-bulk-review/claims-bulk-review';
import { ClaimsReviewStore } from './claims-review.store';

/**
 * Claims a model proposed and a person decides (phase 10, ADR 0018), as R-3
 * drew the lists of work (R-4.3): the queue opens on the weakly supported —
 * the ones a person is for — keeps both decisions on the row, and opens a
 * claim beside the list with the statement in both languages, every quote that
 * backs it and how well, and who proposed it.
 *
 * **A product's claims together, through the review** (Steward's decision 5,
 * which amends R-2's P4): R-2 refused a bulk approve because it is how a review
 * queue becomes a rubber stamp; the review that shows every row, any row
 * unticked, and the server refusing a weak claim in bulk, is what answers that.
 * Nothing reaches the catalogue until a person approves it (invariant 12).
 */
@Component({
  selector: 'backoffice-claims-review-page',
  imports: [ClaimList, ClaimPane, ClaimsBulkReview],
  templateUrl: './claims-review-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list and the pane have of their own is in their folders.
  host: { class: 'block', '(document:keydown)': 'key($event)' },
  providers: [ClaimsReviewStore],
})
export class ClaimsReviewPage {
  protected readonly store = inject(ClaimsReviewStore);
  private readonly usage = inject(BackofficeUsage);
  private readonly bulk = viewChild.required(ClaimsBulkReview);

  /** A product's claims together, the open claim's product (S-5). */
  protected reviewAll(claim: ClaimForReview): void {
    this.bulk().open(claim.productId, this.store.productName(claim));
  }

  /** j and k move down and up the claims, the pane following (the redesign's R-3, P5). */
  protected key(event: KeyboardEvent): void {
    const step = stepOf(event, !!document.querySelector('dialog[open]'));
    if (!step) return;
    const claims = this.store.claims();
    const index = landing(
      claims.findIndex((claim) => claim.claimId === this.store.selectedId()),
      claims.length,
      step,
    );
    if (index === null) return;
    event.preventDefault();
    this.usage.affordance('keys', step === 1 ? 'next' : 'previous');
    this.store.open(claims[index]);
  }
}
