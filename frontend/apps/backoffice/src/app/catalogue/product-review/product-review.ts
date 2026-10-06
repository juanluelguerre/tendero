import { PricePipe } from '@tendero/shared-ui';
import { NgOptimizedImage } from '@angular/common';
import { Component, inject, input, linkedSignal } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { ProductImageUrls } from '@tendero/shared-util';
import { ProductReviewStore } from './product-review.store';

/** One picture's alt text while a person types it. */
interface AltDraft {
  id: string;
  es: string;
  en: string;
}

/** The cultures a product is written in, in the order the review shows them. */
const Cultures = ['es', 'en'] as const;

/**
 * What a person looks at before a product goes on sale, or comes off it (the
 * redesign's R-4.3): the images, the name and description in every culture as
 * stored, with a missing one said to be missing rather than filled from the
 * other, the attributes and the price — and, under them, what each picture says in
 * each language, in boxes: where a shopkeeper types alt text with no model at all. The review queue and the products screen
 * both open it in their pane; the actions are theirs.
 */
@Component({
  selector: 'backoffice-product-review',
  imports: [TranslocoDirective, NgOptimizedImage, PricePipe, FormField],
  templateUrl: './product-review.html',
  host: { class: 'grid gap-4' },
  providers: [ProductReviewStore],
})
export class ProductReview {
  protected readonly store = inject(ProductReviewStore);
  protected readonly culture = inject(CultureStore);
  protected readonly images = inject(ProductImageUrls);

  readonly code = input.required<string>();

  protected readonly cultures = Cultures;

  /**
   * What each picture says, as it is being typed: input that lives while it is
   * typed, so a signal of this component's (ADR 0051), written again from the
   * product each time it is read — after a save, or when another product opens.
   */
  protected readonly alts = linkedSignal<AltDraft[]>(() =>
    (this.store.review.value()?.images ?? []).map((image) => ({
      id: image.id,
      es: image.alt['es'] ?? '',
      en: image.alt['en'] ?? '',
    })),
  );
  protected readonly altsForm = form(this.alts);

  /** Whether the words in a picture's boxes are no longer the ones stored. */
  protected changed(index: number): boolean {
    const stored = this.store.review.value()?.images[index]?.alt ?? {};
    const draft = this.alts()[index];
    return !!draft && (draft.es !== (stored['es'] ?? '') || draft.en !== (stored['en'] ?? ''));
  }

  protected save(productId: string, index: number): void {
    const draft = this.alts()[index];
    if (draft) this.store.describe(productId, draft.id, { es: draft.es, en: draft.en });
  }

  constructor() {
    this.store.follow(this.code);
  }
}
