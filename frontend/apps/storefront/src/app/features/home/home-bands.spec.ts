import type { CategoryView, OfferView, SearchHit } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import {
  departmentsOf,
  freeShippingOf,
  labelled,
  offerRulesOf,
  rankedIds,
  rankedItems,
  risingItems,
  suggestionItems,
} from './home-bands';

describe('what the home bands make of their answers', () => {
  it('keeps a ranking in its order, with its places, and skips a product no longer shown', () => {
    const ranked = {
      days: 7,
      items: [
        { rank: 2, productId: 'b' },
        { rank: 1, productId: 'a' },
        { rank: 3, productId: 'gone' },
      ],
    };

    expect(rankedIds(ranked)).toEqual(['a', 'b', 'gone']);
    expect(rankedItems(ranked, [hit('b'), hit('a')]).map((item) => [item.hit.productId, item.rank])).toEqual([
      ['a', 1],
      ['b', 2],
    ]);
  });

  it('says why a suggestion is there by the viewed product name, and never makes one up', () => {
    const items = suggestionItems(
      {
        items: [
          { product: hit('x'), becauseOf: 'a' },
          { product: hit('y'), becauseOf: 'UNKNOWN' },
        ],
        rankedBy: 'Rule',
      },
      [hit('a')],
      (name) => `Porque viste ${name}`,
    );

    expect(items[0].reason).toEqual({ icon: 'viewed', text: 'Porque viste Product a' });
    expect(items[1].reason).toBeNull();
  });

  it('labels and explains a whole row, dropping a rank that belongs to another row', () => {
    expect(labelled([hit('a')], 'Oferta')[0].badge).toBe('Oferta');
    expect(risingItems([{ hit: hit('a'), rank: 1 }], 'Sube esta semana')[0]).toEqual({
      hit: hit('a'),
      rank: null,
      reason: { icon: 'rising', text: 'Sube esta semana' },
    });
  });

  it('takes the free-shipping promise from the promotion own name, and the rest as small print', () => {
    const offers = [
      offer('SHIP0', 'Envío gratis desde 50 €', 'free-shipping'),
      offer('A', '10% en ropa'),
      offer('B', '3x2 en camisetas'),
    ];

    expect(freeShippingOf(offers)).toBe('Envío gratis desde 50 €');
    expect(freeShippingOf([offer('A', '10% en ropa')])).toBeNull();
    expect(offerRulesOf(offers)).toBe('10% en ropa · 3x2 en camisetas');
  });

  it('lists the departments with a few of their sections', () => {
    const tree: CategoryView[] = [
      category('HOME', null, 0),
      ...['A', 'B', 'C', 'D', 'E'].map((code) => category(code, 'HOME', 1)),
      category('BAGS', null, 0),
    ];

    expect(departmentsOf(tree)).toEqual([
      { code: 'HOME', name: 'Name of HOME', sections: 'Name of A · Name of B · Name of C · Name of D' },
      { code: 'BAGS', name: 'Name of BAGS', sections: '' },
    ]);
  });
});

function category(code: string, parent: string | null, depth: number): CategoryView {
  return { code, parent, name: `Name of ${code}`, path: [], depth };
}

function offer(code: string, name: string, kind = 'percent-off-line'): OfferView {
  return { code, name, kind, percent: null, amountOff: null, buy: null, free: null, until: null };
}

function hit(id: string): SearchHit {
  return {
    productId: id,
    code: id.toUpperCase(),
    name: `Product ${id}`,
    slug: `product-${id}`,
    brand: null,
    category: null,
    matchedVariantId: `${id}-DEFAULT`,
    matchedSku: `${id}-DEFAULT`,
    price: { amount: 1990, currency: 'EUR' },
    priceFrom: { amount: 1990, currency: 'EUR' },
    priceTo: { amount: 1990, currency: 'EUR' },
    imageId: null,
    inStock: true,
    score: 1,
  };
}
