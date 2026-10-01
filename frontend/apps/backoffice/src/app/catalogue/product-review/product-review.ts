import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, ProductImageUrls, valueOf } from '@tendero/shared-util';
import { ProductReviewStore } from './product-review.store';

/** The cultures a product is written in, in the order the review shows them. */
const Cultures = ['es', 'en'] as const;

/**
 * What a person looks at before a product goes on sale, or comes off it (the
 * redesign's R-4.3): the images, the name and description in every culture as
 * stored, with a missing one said to be missing rather than filled from the
 * other, the attributes and the price. The review queue and the products screen
 * both open it in their pane; the actions are theirs.
 */
@Component({
  selector: 'backoffice-product-review',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-review.html',
  styleUrl: './product-review.css',
  providers: [ProductReviewStore],
})
export class ProductReview {
  /** A resource read without throwing when its request failed (Angular 22). */
  protected readonly valueOf = valueOf;

  protected readonly store = inject(ProductReviewStore);
  private readonly culture = inject(CultureStore);
  protected readonly images = inject(ProductImageUrls);

  readonly code = input.required<string>();

  protected readonly cultures = Cultures;

  constructor() {
    this.store.follow(this.code);
  }

  protected price(amount: number, currency: string): string {
    return formatPrice(amount, currency, this.culture.active());
  }
}
