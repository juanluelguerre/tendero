import { Component, inject, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { ReviewDialog } from '../../../worklist/review-dialog/review-dialog';
import { statementIn } from '../claim-statement';
import { ClaimsBulkReviewStore } from './claims-bulk-review.store';

let reviews = 0;

/**
 * A product's claims reviewed together (Steward spec, *The bulk review of
 * claims*; decision 5, which amends R-2's P4), in the frame every review shares:
 * each claim's attribute, value and the words it rests on, ticked — except a
 * weakly supported one, which cannot go together and is opened on its own. The
 * foot says how many can go together, offers to open the weak one, and Approve.
 */
@Component({
  selector: 'backoffice-claims-bulk-review',
  imports: [TranslocoDirective, ReviewDialog],
  templateUrl: './claims-bulk-review.html',
  providers: [ClaimsBulkReviewStore],
})
export class ClaimsBulkReview {
  protected readonly store = inject(ClaimsBulkReviewStore);
  protected readonly culture = inject(CultureStore);

  /** Something was approved: the queue reads itself again. */
  readonly decided = output<void>();
  /** The person chose to decide a weak claim on its own. */
  readonly openClaim = output<string>();

  protected readonly headingId = `claims-review-${++reviews}`;
  protected readonly statementIn = statementIn;
  protected readonly product = signal('');
  private readonly frame = viewChild.required(ReviewDialog);

  open(productId: string, productName: string): void {
    this.product.set(productName);
    this.store.load(productId);
    this.frame().open();
  }

  protected close(): void {
    this.frame().close();
  }

  protected approve(): void {
    this.store.approve(() => this.decided.emit());
  }

  protected openOne(claimId: string): void {
    this.frame().close();
    this.openClaim.emit(claimId);
  }
}
