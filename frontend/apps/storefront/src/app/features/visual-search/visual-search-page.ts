import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { Icon } from '@tendero/shared-ui';
import { ProductCard } from '../../product-card/product-card';
import { ShopLinks } from '../../shop-links';
import { VisualSearchStore } from '../../visual-search/visual-search.store';

/**
 * The answer to a picture (phase 13a; the mock-up, screen 4): **the nearest
 * things we have**, never "we found". A nearest picture always exists, so the
 * page promises what is true — these are the products that look most like the
 * picture, and the shop may not sell the thing itself.
 *
 * It is not the words' listing: there are no filters, no order to choose and no
 * count, because the order is the answer. Each card is the shop's own, drawn
 * with the picture that matched — the colour that looked like it, when the
 * product comes in several.
 *
 * The picture and its answer live in the root store, in the browser's memory
 * only; a page reloaded has neither, and says how to ask again.
 */
@Component({
  selector: 'storefront-visual-search-page',
  imports: [TranslocoDirective, RouterLink, ProductCard, Icon],
  templateUrl: './visual-search-page.html',
  // A product followed is a click on a link inside one of the cards: heard here, as the home's rows hear theirs.
  host: { class: 'block', '(click)': 'clicked($event)' },
})
export class VisualSearchPage {
  protected readonly store = inject(VisualSearchStore);
  protected readonly links = inject(ShopLinks);

  /** The cards, each showing the picture that matched rather than the product's cover. */
  protected readonly cards = computed<readonly SearchHit[] | null>(
    () => this.store.matches()?.map((match) => ({ ...match.product, imageId: match.picture })) ?? null,
  );

  protected clicked(event: Event): void {
    if (!(event.target instanceof Element)) return;
    const card = event.target.closest('a')?.closest('[data-position]');
    const position = Number(card?.getAttribute('data-position'));
    if (position > 0) this.store.followed(position);
  }
}
