import { Component, inject, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProductImageView } from '@tendero/shared-api';
import { Icon } from '@tendero/shared-ui';
import { ProductImageUrls } from '@tendero/shared-util';
import { ProductImage } from '../product-image/product-image';

/** How far a finger has to travel, in CSS pixels, before a touch is a swipe and not a tap. */
const Swipe = 40;

/**
 * A product's pictures: the one in front, an arrow either side of it, a count,
 * and a row of thumbnails (the gallery's mock-up, 2026-10-15).
 *
 * The thumbnails **never move**. The page used to bring the one pressed to the
 * front of the row, so the row reordered itself under the shopper's finger; now
 * the one in front is marked where it is. The arrows show on hover or when the
 * keyboard is in the gallery, and always on a touch screen, where there is no
 * hover; a swipe and ← → do the same. They wrap round, and with one picture
 * there are none, nor a count, nor a row.
 *
 * It shows and it asks: `show` and `step` go to the page's store, which knows the
 * gallery, its order and which picture is next. Neutral, never clay: the page's
 * one accent action is *Añadir al carrito*. The storefront's for now; it moves to
 * `libs/shared/ui` the day a second app shows a gallery.
 */
@Component({
  selector: 'storefront-product-gallery',
  imports: [TranslocoDirective, ProductImage, Icon],
  templateUrl: './product-gallery.html',
  host: { class: 'block' },
})
export class ProductGallery {
  /** The pictures, in the order they are shown; that order never changes with the one in front. */
  readonly images = input.required<readonly ProductImageView[]>();
  /** The picture in front; undefined when the product has none, and the placeholder is drawn. */
  readonly current = input.required<ProductImageView | undefined>();
  /** Where it is, counted from one: *3 / 4*. */
  readonly position = input.required<{ readonly index: number; readonly total: number }>();
  /** The product's name: what a picture without words of its own says. */
  readonly name = input.required<string>();

  /** A thumbnail pressed, by its image's id. */
  readonly show = output<string>();
  /** An arrow, a key or a swipe: one picture on (1) or back (-1). */
  readonly step = output<1 | -1>();

  private readonly urls = inject(ProductImageUrls);
  private pressedAt: number | null = null;

  protected url(imageId: string): string {
    return this.urls.of(imageId);
  }

  protected key(event: KeyboardEvent): void {
    if (this.images().length < 2) return;
    if (event.key === 'ArrowRight') this.step.emit(1);
    else if (event.key === 'ArrowLeft') this.step.emit(-1);
    else return;
    event.preventDefault();
  }

  protected down(event: PointerEvent): void {
    this.pressedAt = event.clientX;
  }

  /** The browser took the touch (a scroll, a pinch): no swipe. */
  protected cancel(): void {
    this.pressedAt = null;
  }

  protected up(event: PointerEvent): void {
    const from = this.pressedAt;
    this.pressedAt = null;
    if (from === null || this.images().length < 2) return;
    const travelled = event.clientX - from;
    // A finger moved left drags the next picture in, as on every phone.
    if (Math.abs(travelled) >= Swipe) this.step.emit(travelled < 0 ? 1 : -1);
  }
}
