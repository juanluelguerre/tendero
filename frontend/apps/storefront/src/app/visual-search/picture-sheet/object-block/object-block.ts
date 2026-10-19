import { Component, computed, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { Icon } from '@tendero/shared-ui';
import { ProductCard } from '../../../product-card/product-card';
import type { SheetBlock, SheetObject } from '../picture-sheet.store';

/** How many of an object's products its block shows; *See all* opens the rest (CS-3). */
export const CardsInABlock = 4;

/**
 * One object of the picture and the shop's products nearest it (CS-3; the
 * mock-up, screen 9): its number and kind as the heading, up to four cards
 * worded as **similar** — never *the same* — and *See all*. Narrowed by words
 * to nothing, it says so with a way back (CS-4); failed, it says so and can be
 * asked again alone, while the other blocks stand (CS-9).
 */
@Component({
  selector: 'storefront-object-block',
  imports: [TranslocoDirective, ProductCard, Icon],
  templateUrl: './object-block.html',
  // A product followed is a click on a link inside a card: heard here, as the page of results hears its own.
  host: { class: 'block', '(click)': 'clicked($event)' },
})
export class ObjectBlock {
  readonly object = input.required<SheetObject>();
  readonly block = input.required<SheetBlock | undefined>();
  readonly words = input('');
  /** The heading was pressed: its object is outlined on the picture. */
  readonly named = output<number>();
  readonly seeAll = output<number>();
  readonly retry = output<number>();
  readonly unnarrow = output<void>();
  /** A product was opened from the block, from its place in it. */
  readonly followed = output<number>();

  /** The cards, each showing the picture that matched rather than the product's cover. */
  protected readonly cards = computed<readonly SearchHit[]>(
    () =>
      this.block()
        ?.matches.slice(0, CardsInABlock)
        .map((match) => ({ ...match.product, imageId: match.picture })) ?? [],
  );

  protected clicked(event: Event): void {
    if (!(event.target instanceof Element)) return;
    const card = event.target.closest('a')?.closest('[data-position]');
    const position = Number(card?.getAttribute('data-position'));
    if (position > 0) this.followed.emit(position);
  }
}
