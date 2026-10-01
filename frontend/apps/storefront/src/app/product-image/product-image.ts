import { ChangeDetectionStrategy, Component, input, linkedSignal, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { ProductImagePlaceholder } from '../product-image-placeholder/product-image-placeholder';

/**
 * A product's picture, or the shop's placeholder when it has none or it does not load.
 *
 * The card, the product page's gallery and the order history each wrote the same
 * three steps: show the image, remember that it failed, draw the placeholder
 * instead. The seed points at pictures that may not be there, so the failure is
 * the DEFAULT path on a fresh clone rather than an error, and it is the step a
 * copy forgets. Here it is written once (ADR 0049).
 *
 * **The caller shapes the host** — size, aspect, border, radius, background and
 * `object-fit`, as utilities on the element — and the picture or the placeholder
 * fills it. The image inherits the host's `object-fit`, so a caller that crops on
 * a desktop and contains on a phone says so once, on the box it owns.
 *
 * A failure is remembered for the address that failed: a new `src` (another
 * thumbnail pressed) is tried afresh.
 */
@Component({
  selector: 'storefront-product-image',
  imports: [TranslocoDirective, ProductImagePlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-image.html',
  // The host is the caller's box: it is a block, and what it holds is clipped to its
  // radius.
  host: { class: 'block overflow-hidden' },
})
export class ProductImage {
  /** The picture's address, or null when the product has none. */
  readonly src = input<string | null>(null);
  /** What the picture says to a screen reader; empty when the words beside it already do. */
  readonly alt = input.required<string>();
  /** Whether the placeholder writes *Sin imagen* under its mark; a thumbnail has no room for it. */
  readonly labelled = input(false);
  readonly loading = input<'eager' | 'lazy'>('eager');

  /** The picture did not load: for an owner that decides more than this box (the order history's strip). */
  readonly failed = output<void>();

  protected readonly broken = linkedSignal({ source: this.src, computation: () => false });

  protected fail(): void {
    this.broken.set(true);
    this.failed.emit();
  }
}
