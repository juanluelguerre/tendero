import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { CartStore } from './data-access/cart.service';
import { ProductImagePlaceholder } from './product-image-placeholder';
import { ShopLinks } from './shop-links';

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
  imports: [TranslocoDirective, RouterLink, ProductImagePlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './product-card.html',
  styleUrl: './product-card.css',
  host: { '[class.card--soldOut]': '!hit().inStock' },
})
export class ProductCard {
  readonly hit = input.required<SearchHit>();

  private readonly cart = inject(CartStore);
  private readonly culture = inject(CultureStore);

  protected readonly links = inject(ShopLinks);
  protected readonly adding = signal(false);

  /** The seed points at images that may not be there, so a broken one is the
   *  default path on a fresh clone rather than an error. */
  protected readonly broken = signal(false);

  protected imageUrl(): string | null {
    const id = this.hit().imageId;
    return id && !this.broken() ? `/api/images/${id}` : null;
  }

  protected price(): string {
    return formatPrice(this.hit().priceAmount, this.hit().priceCurrency, this.culture.active());
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
