import { describe, expect, it } from 'vitest';
import type { ProductDetail, VariantView } from '@tendero/shared-api';
import { availabilityOf, initialSelection, select, sellable, variantFor } from './variant-picker';

/**
 * The picker's rules, which are the only real logic on the product page.
 *
 * The catalogue under test is deliberately incomplete, because a complete one
 * proves nothing: navy comes in 38 and 40, black only in 38, and the 40 in navy
 * is out of stock. Every interesting case in a variant picker is a hole in the
 * matrix.
 */
function aShirt(): ProductDetail {
  return {
    productId: 'p1',
    code: 'K7M2QX9P4T',
    name: 'Camisa de lino',
    slug: 'camisa-de-lino',
    description: null,
    brand: null,
    category: [],
    images: [],
    attributes: [],
    axes: [
      {
        code: 'COLOR',
        label: 'Color',
        options: [
          { code: 'NAVY_BLUE', label: 'Azul marino' },
          { code: 'BLACK', label: 'Negro' },
        ],
      },
      {
        code: 'SIZE',
        label: 'Talla',
        options: [
          { code: '38', label: '38' },
          { code: '40', label: '40' },
        ],
      },
    ],
    variants: [
      variant('NAVY-38', { COLOR: 'NAVY_BLUE', SIZE: '38' }, { inStock: true }),
      variant('NAVY-40', { COLOR: 'NAVY_BLUE', SIZE: '40' }, { inStock: false }),
      variant('BLACK-38', { COLOR: 'BLACK', SIZE: '38' }, { inStock: true }),
    ],
    priceFrom: 29.9,
    priceTo: 34.9,
    priceCurrency: 'EUR',
    alternates: [],
    missingCultures: [],
    status: 'active',
    updatedAt: '2026-09-03T00:00:00Z',
  } as unknown as ProductDetail;
}

function variant(
  sku: string,
  axisValues: Record<string, string>,
  options: { inStock: boolean; discontinued?: boolean },
): VariantView {
  return {
    variantId: sku,
    sku,
    priceAmount: 29.9,
    priceCurrency: 'EUR',
    axisValues,
    label: Object.values(axisValues).join(' · '),
    imageId: null,
    discontinued: options.discontinued ?? false,
    inStock: options.inStock,
    remaining: null,
  } as unknown as VariantView;
}

describe('the variant picker', () => {
  it('opens on something that can actually be bought', () => {
    const product = aShirt();

    expect(initialSelection(product)).toEqual({ COLOR: 'NAVY_BLUE', SIZE: '38' });
  });

  it('opens on a sold-out variant only when there is nothing else', () => {
    const product = aShirt();
    product.variants.forEach((v) => ((v as { inStock: boolean }).inStock = false));

    // Not an empty picker: a product with no stock at all still has to render
    // its options, or the page says nothing about what the shop sells.
    expect(initialSelection(product)).toEqual({ COLOR: 'NAVY_BLUE', SIZE: '38' });
  });

  /**
   * The case the whole design exists for. Black comes in 38 and not in 40, so
   * with 38 chosen black is offered — and with 40 chosen it is DISABLED rather
   * than removed.
   */
  it('disables a combination that is not sold, holding the other axis fixed', () => {
    const variants = sellable(aShirt());

    expect(availabilityOf(variants, { COLOR: 'NAVY_BLUE', SIZE: '38' }, 'COLOR', 'BLACK')).toBe(
      'available',
    );

    expect(availabilityOf(variants, { COLOR: 'NAVY_BLUE', SIZE: '40' }, 'COLOR', 'BLACK')).toBe(
      'unavailable',
    );
  });

  /**
   * Sold out is not the same as not sold, and the picker must not collapse them.
   * Navy in 40 exists; there is none left. It stays selectable so the shopper
   * can see that answer rather than concluding the size is not made.
   */
  it('tells sold out apart from not sold', () => {
    const variants = sellable(aShirt());

    expect(availabilityOf(variants, { COLOR: 'NAVY_BLUE', SIZE: '38' }, 'SIZE', '40')).toBe(
      'soldOut',
    );

    expect(availabilityOf(variants, { COLOR: 'BLACK', SIZE: '38' }, 'SIZE', '40')).toBe(
      'unavailable',
    );
  });

  it('resolves a full selection to one variant', () => {
    const variants = sellable(aShirt());

    expect(variantFor(variants, { COLOR: 'BLACK', SIZE: '38' })?.sku).toBe('BLACK-38');
    expect(variantFor(variants, { COLOR: 'BLACK', SIZE: '40' })).toBeNull();
  });

  /**
   * Choosing something that does not exist in the current size moves the SIZE,
   * never the colour that was just pressed. A picker that changed what you
   * clicked would be worse than one that left you on a dead combination.
   */
  it('repairs the rest of the selection and never the axis that was clicked', () => {
    const variants = sellable(aShirt());

    const after = select(variants, { COLOR: 'NAVY_BLUE', SIZE: '40' }, 'COLOR', 'BLACK');

    expect(after['COLOR']).toBe('BLACK');
    expect(after['SIZE']).toBe('38');
    expect(variantFor(variants, after)?.sku).toBe('BLACK-38');
  });

  it('leaves a valid selection alone', () => {
    const variants = sellable(aShirt());

    expect(select(variants, { COLOR: 'NAVY_BLUE', SIZE: '38' }, 'SIZE', '40')).toEqual({
      COLOR: 'NAVY_BLUE',
      SIZE: '40',
    });
  });

  /**
   * A discontinued variant is not on sale, so it takes no part in the picker.
   * The aggregate keeps it because an old order still names it, which is a
   * different question from what the shop sells today.
   */
  it('ignores discontinued variants entirely', () => {
    const product = aShirt();
    product.variants.push(
      variant('BLACK-40', { COLOR: 'BLACK', SIZE: '40' }, { inStock: true, discontinued: true }),
    );

    const variants = sellable(product);

    expect(variants).toHaveLength(3);
    expect(availabilityOf(variants, { COLOR: 'BLACK', SIZE: '38' }, 'SIZE', '40')).toBe(
      'unavailable',
    );
  });

  /** A product with one implicit default variant has no picker to render. */
  it('handles a product with no axes', () => {
    const product = aShirt();
    product.axes = [];
    product.variants = [variant('DEFAULT', {}, { inStock: true })];

    expect(initialSelection(product)).toEqual({});
    expect(variantFor(sellable(product), {})?.sku).toBe('DEFAULT');
  });
});
