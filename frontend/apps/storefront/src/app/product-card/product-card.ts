import { PricePipe } from '@tendero/shared-ui';
import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { ProductImageUrls } from '@tendero/shared-util';
import { CartStore } from '../cart/cart.store';
import { CompareStore } from '../compare/compare.store';
import type { ProductVia } from '../features/product/arrived-from';
import { ProductImage } from '../product-image/product-image';
import { ShopLinks } from '../shop-links';
import { dealWords } from './deal-words';

/** Why a card is in its row, in one line under the price: *Porque viste…*, *Sube esta semana* (R-8). */
export interface CardReason {
  readonly icon: 'viewed' | 'rising';
  readonly text: string;
}

/**
 * One product, as a card.
 *
 * It was inside the search page, which was fine while search was the only way
 * into the catalogue. It is not any more: a home page shows rows of these, a
 * category page shows a grid of them, and a card copied into three templates is
 * three cards that drift — the sold-out rule gets fixed in one of them, the
 * placeholder in another.
 *
 * **It owns adding to the cart**, and that is the part worth keeping together.
 * The SKU comes from the search hit, which is the winning VARIANT (ADR 0015) —
 * a card that only knew a product id would have to guess which size somebody
 * meant, and that guess would then have to be repeated everywhere a card
 * appears.
 */
@Component({
  selector: 'storefront-product-card',
  imports: [TranslocoDirective, RouterLink, ProductImage, PricePipe],
  templateUrl: './product-card.html',
  styleUrl: './product-card.css',
  host: {
    '[class.card--line]': "layout() === 'line'",
  },
})
export class ProductCard {
  readonly hit = input.required<SearchHit>();

  /**
   * The rows of the home page (R-8) ask more of a card than the listing does,
   * and they get it as inputs on THIS card rather than as copies of it: a badge,
   * a reason, a rank, a quieter card without its buttons, and the row that led
   * to the product for `product_viewed`.
   */
  /** A short label on the picture: *Oferta*, *Nuevo*. */
  readonly badge = input<string | null>(null);
  /** Why the card is here, with its icon. */
  readonly reason = input<CardReason | null>(null);
  /** Its place in a ranking, drawn large and never with a count: *Lo más vendido*. */
  readonly rank = input<number | null>(null);
  /** Add to cart and compare; off where the card is a way in rather than a shelf (the campaign card). */
  readonly actions = input(true);
  /** A tile, or a line — picture beside the words — for a ranking. */
  readonly layout = input<'tile' | 'line'>('tile');
  /** The row that led here, carried in the navigation's state (`arrivedVia`). */
  readonly via = input<ProductVia | null>(null);
  /** The first row of a listing: its pictures are what the page is judged loaded by (LCP), fetched first. */
  readonly priority = input(false);

  private readonly cart = inject(CartStore);
  protected readonly culture = inject(CultureStore);
  private readonly images = inject(ProductImageUrls);

  protected readonly links = inject(ShopLinks);
  protected readonly compare = inject(CompareStore);
  protected readonly adding = signal(false);

  /** The first promotion it is in, as the card says it under the price (phase 13c); none when it is in none. */
  protected readonly deal = computed(() => {
    const first = this.hit().deals?.[0];
    return first ? dealWords(first, this.culture.active()) : null;
  });

  protected linkState(): { via: ProductVia } | undefined {
    const via = this.via();
    return via ? { via } : undefined;
  }

  protected imageUrl(): string | null {
    const id = this.hit().imageId;
    return id ? this.images.of(id) : null;
  }

  /** Picks the product for the comparison tray, or takes it out (P10-7). */
  protected toggleCompare(): void {
    this.compare.toggle({ code: this.hit().code, name: this.hit().name });
  }

  protected async addToCart(): Promise<void> {
    this.adding.set(true);
    try {
      await this.cart.add(this.hit().matchedSku, 1);
    } finally {
      this.adding.set(false);
    }
  }
}
