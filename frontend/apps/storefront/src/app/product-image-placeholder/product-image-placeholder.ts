import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * What a product with no picture shows.
 *
 * **It replaces a letter in a circle**, which was the shop drawing the first
 * character of the product name on a coloured tile. That reads as a placeholder
 * for a photograph that failed, and it is not: eight of the hundred sample
 * products have artwork and the rest have none yet, so this is the DEFAULT path
 * on a fresh clone rather than an error state. A grid of initials looks like
 * something broke; a grid of the shop's own mark looks like a catalogue still
 * being filled.
 *
 * It is one component for three surfaces — the result card, the product page
 * gallery and the thumbnails in a shopper's order history — because a
 * placeholder that differs per screen is three placeholders to keep in step.
 * Everything that varies is a token the caller sets in CSS: size, colour and
 * whether the words appear.
 *
 * It lives at the app root beside `shop-links.ts` for the reason that one does:
 * three features use it, and a feature reaching into a sibling is the frontend
 * version of the rule slices already follow.
 */
@Component({
  selector: 'storefront-product-image-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-image-placeholder.html',
  styleUrl: './product-image-placeholder.css',
  host: {
    role: 'img',
    '[attr.aria-label]': 'alt()',
  },
})
export class ProductImagePlaceholder {
  /**
   * What a screen reader says. Always present, because "no image" is
   * information a sighted reader gets from the picture being absent.
   */
  readonly alt = input.required<string>();

  /**
   * The words under the mark, or empty to draw the mark alone.
   *
   * A 48-pixel thumbnail has no room for a sentence, and shrinking one until it
   * fits produces something nobody can read and everybody has to look at.
   */
  readonly label = input('');

  /**
   * `clipPath` is referenced by id, and an id has to be unique in the document —
   * a page showing twenty of these would otherwise have twenty elements called
   * the same thing, and the browser resolves all of them to the first.
   */
  protected readonly clipId = `awning-${Math.random().toString(36).slice(2, 9)}`;
}
