import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProductSummary } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { ListState } from '../../../worklist/list-state/list-state';
import { ReviewQueueStore } from '../review-queue.store';

/**
 * The drafts waiting to be published (R-4.1): a table from a tablet up, cards on
 * a phone, each with its languages and a quiet Publish. It reads and acts
 * through the page's store, which the pane shares.
 */
@Component({
  selector: 'backoffice-review-list',
  imports: [RouterLink, TranslocoDirective, NgTemplateOutlet, ListState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './review-list.html',
  styleUrl: './review-list.css',
})
export class ReviewList {
  protected readonly store = inject(ReviewQueueStore);
  private readonly culture = inject(CultureStore);

  protected price(item: ProductSummary): string {
    return formatPrice(item.priceAmount, item.priceCurrency, this.culture.active());
  }
}
