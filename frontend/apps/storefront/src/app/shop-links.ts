import { computed, inject, Injectable } from '@angular/core';
import { CultureStore } from '@tendero/shared-i18n';

/**
 * Every link in the shop, built with the language segment on the front.
 *
 * It exists because `P5-13` made the URL carry the culture, and an absolute
 * `routerLink="/cart"` then points at a route that does not exist — the shop
 * lives under `/es` and `/en` now. Relative links would resolve correctly from
 * most places and not from all of them, and "correct depending on where the
 * component happens to be mounted" is the kind of rule that holds until somebody
 * moves a component.
 *
 * One place, so adding a third culture changes the route table and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class ShopLinks {
  private readonly culture = inject(CultureStore).active;

  readonly home = computed(() => ['/', this.culture()]);
  readonly cart = computed(() => ['/', this.culture(), 'cart']);
  readonly checkout = computed(() => ['/', this.culture(), 'checkout']);
  readonly account = computed(() => ['/', this.culture(), 'account']);

  /**
   * A department or a section. The CODE is in the URL and not the name, for the
   * reason ADR 0026 gives about products: a name is not stable and does not
   * survive a translation, and this one has two of them.
   */
  category(code: string): unknown[] {
    return ['/', this.culture(), 'c', code];
  }

  product(slug: string, code: string): unknown[] {
    return ['/', this.culture(), 'p', slug, code];
  }

  order(id: string): unknown[] {
    return ['/', this.culture(), 'orders', id];
  }

  /** The same path in another language, which is what `hreflang` points at. */
  productIn(culture: string, slug: string, code: string): string {
    return `/${culture}/p/${slug}/${code}`;
  }
}
