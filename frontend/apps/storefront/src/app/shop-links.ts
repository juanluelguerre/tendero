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
  /** The account's address book, and a new entry in it (P15-2). */
  readonly addresses = computed(() => ['/', this.culture(), 'account', 'addresses']);
  readonly newAddress = computed(() => ['/', this.culture(), 'account', 'addresses', 'new']);
  /** The account's saved ways to pay (P15-4). */
  readonly waysToPay = computed(() => ['/', this.culture(), 'account', 'ways-to-pay']);
  /** The account's preferences and the shopper's own data (P15-6, P15-9). */
  readonly preferences = computed(() => ['/', this.culture(), 'account', 'preferences']);
  readonly yourData = computed(() => ['/', this.culture(), 'account', 'your-data']);
  /** The comparison; the products travel in `?codes=`, so it can be shared (P10-7). */
  readonly compare = computed(() => ['/', this.culture(), 'compare']);
  /** *Compra con tu agente*: how to point an assistant at the shop (R-8, H-5). */
  readonly agents = computed(() => ['/', this.culture(), 'agents']);
  readonly kits = computed(() => ['/', this.culture(), 'kits']);

  /**
   * A department or a section. The CODE is in the URL and not the name, for the
   * reason ADR 0026 gives about products: a name is not stable and does not
   * survive a translation, and this one has two of them.
   */
  kit(code: string): unknown[] {
    return ['/', this.culture(), 'kits', code];
  }

  category(code: string): unknown[] {
    return ['/', this.culture(), 'c', code];
  }

  product(slug: string, code: string): unknown[] {
    return ['/', this.culture(), 'p', slug, code];
  }

  /** One entry of the address book, to edit it. */
  address(id: string): unknown[] {
    return ['/', this.culture(), 'account', 'addresses', id];
  }

  order(id: string): unknown[] {
    return ['/', this.culture(), 'orders', id];
  }

  /**
   * The same page in another language: the first segment of the URL swapped,
   * its query kept. Switching language navigates, because the URL is what carries
   * the culture (`P5-13`), so a language change is a page a person can share.
   */
  sameIn(url: string, culture: string): string {
    const [path, query] = url.split('?');
    const segments = path.split('/').filter(Boolean);
    segments[0] = culture;
    return `/${segments.join('/')}${query ? `?${query}` : ''}`;
  }

  /** A category's path in a given language; its code is the same in every one. */
  categoryIn(culture: string, code: string): string {
    return `/${culture}/c/${code}`;
  }

  /** The same path in another language, which is what `hreflang` points at. */
  productIn(culture: string, slug: string, code: string): string {
    return `/${culture}/p/${slug}/${code}`;
  }
}
