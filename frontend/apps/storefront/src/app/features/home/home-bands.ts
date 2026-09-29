import type { CategoryView, OfferView, RankedProducts, SearchHit, SimilarProductsResult } from '@tendero/shared-api';
import type { ShelfItem } from '../../shelves/product-row';

/**
 * What the home's bands make of their answers (R-8, H-4), as pure functions: the
 * page composes, these decide. Each takes the server's answer and returns what a
 * row draws, so "which product, which words, in which order" is tested without
 * a component.
 */

/**
 * A ranking's cards in the ranking's order, each with its place: *Lo más
 * vendido* numbered, never counted. A product the cards endpoint left out (no
 * longer shown) leaves a gap in nothing — the places are the ones the server gave.
 */
export function rankedItems(ranked: RankedProducts, hits: readonly SearchHit[]): ShelfItem[] {
  const byId = new Map(hits.map((hit) => [hit.productId, hit]));
  return [...ranked.items]
    .sort((a, b) => a.rank - b.rank)
    .flatMap((item) => {
      const hit = byId.get(item.productId);
      return hit ? [{ hit, rank: item.rank }] : [];
    });
}

/** The ids to ask the cards endpoint for, in the ranking's order. */
export function rankedIds(ranked: RankedProducts): string[] {
  return [...ranked.items].sort((a, b) => a.rank - b.rank).map((item) => item.productId);
}

/**
 * *Puede que te guste*, each saying why: *Porque viste Mochila CityPack 18L*. The
 * why is the server's (`becauseOf`, the code of the viewed product); the page
 * only puts the viewed product's NAME to it, from the trail's own cards. A
 * suggestion whose origin cannot be named is still shown, without a reason —
 * never with a reason made up.
 */
export function suggestionItems(
  similar: SimilarProductsResult,
  viewed: readonly SearchHit[],
  because: (name: string) => string,
): ShelfItem[] {
  const names = new Map(viewed.map((hit) => [hit.code.toUpperCase(), hit.name]));
  return similar.items.map((item) => {
    const name = names.get(item.becauseOf.toUpperCase());
    return { hit: item.product, reason: name ? { icon: 'viewed' as const, text: because(name) } : null };
  });
}

/** The same label on every card of a row: *Oferta*, *Nuevo*. */
export function labelled(hits: readonly SearchHit[], badge: string): ShelfItem[] {
  return hits.map((hit) => ({ hit, badge }));
}

/** The same reason on every card of a row: *Sube esta semana*. */
export function risingItems(items: readonly ShelfItem[], text: string): ShelfItem[] {
  return items.map((item) => ({ ...item, rank: null, reason: { icon: 'rising' as const, text } }));
}

/**
 * The free-shipping promise as the shop sells it: the promotion's own name
 * (*Envío gratis desde 50 €*), so the threshold is the rule's and not the page's.
 */
export function freeShippingOf(offers: readonly OfferView[]): string | null {
  return offers.find((offer) => offer.kind === 'free-shipping')?.name ?? null;
}

/**
 * The offers row's small print: the promotions' names. They are rules; the
 * products are what is bought, so the names go under the heading and not on tiles.
 */
export function offerRulesOf(offers: readonly OfferView[]): string {
  return offers
    .filter((offer) => offer.kind !== 'free-shipping')
    .map((offer) => offer.name)
    .join(' · ');
}

/** A department and a few of its sections, as one line: *Almacenaje · Cocina · Iluminación*. */
export interface Department {
  readonly code: string;
  readonly name: string;
  readonly sections: string;
}

export function departmentsOf(categories: readonly CategoryView[]): Department[] {
  return categories
    .filter((category) => category.depth === 0)
    .map((department) => ({
      code: department.code,
      name: department.name,
      sections: categories
        .filter((category) => category.parent === department.code)
        .slice(0, 4)
        .map((category) => category.name)
        .join(' · '),
    }));
}
