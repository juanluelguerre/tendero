import { PricePipe } from '@tendero/shared-ui';
import { NgTemplateOutlet } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProductSummary } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { ListState } from '../../../worklist/list-state/list-state';
import { ReviewQueueStore } from '../review-queue.store';

/**
 * The drafts waiting to be published (R-4.1): a table from a tablet up, cards on
 * a phone, each with its languages and a quiet Publish. It reads and acts
 * through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-review-list',
  imports: [RouterLink, TranslocoDirective, NgTemplateOutlet, ListState, PricePipe],
  templateUrl: './review-list.html',
})
export class ReviewList {
  protected readonly store = inject(ReviewQueueStore);
  protected readonly culture = inject(CultureStore);
}
