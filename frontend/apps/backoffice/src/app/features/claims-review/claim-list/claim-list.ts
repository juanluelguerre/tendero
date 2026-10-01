import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ClaimForReview } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { FilterChips } from '../../../worklist/filter-chips/filter-chips';
import { ListState } from '../../../worklist/list-state/list-state';
import { Pager } from '../../../worklist/pager/pager';
import { statementIn } from '../claim-statement';
import { ClaimSupport } from '../claim-support/claim-support';
import { ClaimsReviewStore, PageSize } from '../claims-review.store';

/**
 * The claims a model proposed, weakest support first (phase 10): the sentence in
 * the reader's language, the product it is about, its support, and Approve or
 * Reject on the row. It reads and acts through the page's store, which the pane
 * shares.
 */
@Component({
  selector: 'backoffice-claim-list',
  imports: [TranslocoDirective, FilterChips, Pager, ClaimSupport, ListState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './claim-list.html',
  styleUrl: './claim-list.css',
})
export class ClaimList {
  protected readonly store = inject(ClaimsReviewStore);
  private readonly culture = inject(CultureStore);

  protected readonly pageSize = PageSize;

  protected statement(claim: ClaimForReview): string {
    return statementIn(claim, this.culture.active());
  }
}
