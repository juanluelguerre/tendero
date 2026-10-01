import type { CategoryView, FacetOption, SearchFacet } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { NoFilters, toggleOption, withPrice } from './listing-filters';
import {
  brandsToShow,
  categoryLevel,
  childLinks,
  chipsOf,
  isDead,
  keepShape,
  priceLabel,
  safeSwatch,
  sayablePrice,
  sectionsOf,
  type ChipWords,
} from './listing-view';

const option = (value: string, count: number, extra: Partial<FacetOption> = {}): FacetOption => ({
  value,
  label: value,
  count,
  chosen: false,
  ...extra,
});

const facet = (
  code: string,
  kind: SearchFacet['kind'],
  options: FacetOption[],
  extra: Partial<SearchFacet> = {},
): SearchFacet => ({
  code,
  kind,
  label: code,
  options,
  ...extra,
});

const category = (code: string, parent: string | null, depth: number): CategoryView => ({
  code,
  parent,
  name: `Name of ${code}`,
  path: [],
  depth,
});

const words: ChipWords = {
  onOffer: 'On offer',
  newArrivals: 'New',
  inStock: 'In stock',
  money: (amount) => `${amount} €`,
  upTo: (max) => `Up to ${max}`,
  from: (min) => `${min} and above`,
  between: (min, max) => `${min} – ${max}`,
  campaign: (code) => `Campaign: ${code}`,
};

describe('the sections of the column', () => {
  it('draws each facet as what it is, the three switches in one section where the first stands', () => {
    const facets = [
      facet('category', 'Category', [option('BAGS', 3)]),
      facet('brand', 'Brand', [option('Vertex', 2)]),
      facet('price', 'Price', [], { price: { min: 10, max: 90, chosenMin: null, chosenMax: null, bands: [] } }),
      facet('offer', 'Offer', [option('true', 1)]),
      facet('stock', 'InStock', [option('true', 1)]),
      facet('COLOR', 'Axis', [option('COLOR:BLACK', 2, { swatch: '#201D1B' })]),
      facet('SIZE', 'Axis', [option('SIZE:42', 2)]),
      facet('MATERIAL', 'Attribute', [option('MATERIAL:LEATHER', 2)]),
      facet('INDUCTION', 'Flag', [option('INDUCTION:true', 2)]),
    ];

    const sections = sectionsOf(facets);

    expect(sections.map((section) => section.kind)).toEqual([
      'category',
      'brand',
      'price',
      'switches',
      'swatches',
      'sizes',
      'checks',
      'flag',
    ]);
    const switches = sections[3];
    expect(switches.kind === 'switches' && switches.facets.map((item) => item.code)).toEqual(['offer', 'stock']);
  });

  it('leaves out a price facet with no range to draw', () => {
    expect(sectionsOf([facet('price', 'Price', [])])).toEqual([]);
  });

  it('paints only a plain colour from the data', () => {
    expect(safeSwatch('#201D1B')).toBe('#201D1B');
    expect(safeSwatch('red; background: url(x)')).toBeNull();
    expect(safeSwatch(null)).toBeNull();
  });

  it('disables an option with nothing left, unless it is chosen and has to be unchosen', () => {
    expect(isDead(option('A', 0), false)).toBe(true);
    expect(isDead(option('A', 0), true)).toBe(false);
    expect(isDead(option('A', 3), false)).toBe(false);
  });
});

describe('brand, at the scale of hundreds', () => {
  const many = (count: number) =>
    Array.from({ length: count }, (_, index) => option(`Brand ${index + 1}`, 100 - index));

  it('shows the seven with most results and says how many more there are', () => {
    const view = brandsToShow(many(12), NoFilters, false, '');
    expect(view.shown.map((item) => item.value)).toEqual(many(7).map((item) => item.value));
    expect(view.hidden).toBe(5);
    expect(view.findable).toBe(false);
  });

  it('keeps a chosen brand in sight, whatever its rank', () => {
    const view = brandsToShow(many(12), toggleOption(NoFilters, 'Brand', 'Brand 11'), false, '');
    expect(view.shown.map((item) => item.value)).toContain('Brand 11');
    expect(view.hidden).toBe(4);
  });

  it('shows them all once expanded', () => {
    expect(brandsToShow(many(12), NoFilters, true, '').shown).toHaveLength(12);
  });

  it('offers a box past twenty, and finds without case or accents', () => {
    const options = [...many(20), option('Ñandú Montaña', 1, { label: 'Ñandú Montaña' })];
    const view = brandsToShow(options, NoFilters, false, 'nandu mont');
    expect(view.findable).toBe(true);
    expect(view.shown.map((item) => item.value)).toEqual(['Ñandú Montaña']);
  });

  it('offers no box at twenty', () => {
    expect(brandsToShow(many(20), NoFilters, false, 'x').findable).toBe(false);
  });
});

describe('the category, on a search', () => {
  const shop = [
    category('APPAREL', null, 0),
    category('BAGS', null, 0),
    category('BACKPACK', 'BAGS', 1),
    category('LUGGAGE', 'BAGS', 1),
    category('SHOES', 'APPAREL', 1),
  ];

  it('offers the departments the results are in', () => {
    const level = categoryLevel(
      facet('category', 'Category', [option('BAGS', 7), option('APPAREL', 1), option('BACKPACK', 5)]),
      shop,
      NoFilters,
    );
    expect(level.depth).toBe(0);
    expect(level.options.map((item) => item.value)).toEqual(['BAGS', 'APPAREL']);
  });

  it('goes down a level when every result is in one department', () => {
    const level = categoryLevel(
      facet('category', 'Category', [option('BAGS', 7), option('BACKPACK', 5), option('LUGGAGE', 2)]),
      shop,
      NoFilters,
    );
    expect(level.depth).toBe(1);
    expect(level.options.map((item) => item.value)).toEqual(['BACKPACK', 'LUGGAGE']);
  });

  it('keeps a chosen category that is not on the level drawn, so it can be taken off', () => {
    const level = categoryLevel(
      facet('category', 'Category', [option('BAGS', 7), option('APPAREL', 2), option('SHOES', 2)]),
      shop,
      toggleOption(NoFilters, 'Category', 'SHOES'),
    );
    expect(level.options.map((item) => item.value)).toEqual(['BAGS', 'APPAREL', 'SHOES']);
  });
});

describe('the sections of a category page, as links', () => {
  const shop = [category('BAGS', null, 0), category('BACKPACK', 'BAGS', 1), category('LUGGAGE', 'BAGS', 1)];

  it('carries the counts the category facet gave, zero for one it did not name', () => {
    const links = childLinks('BAGS', shop, [facet('category', 'Category', [option('BACKPACK', 5)])]);
    expect(links).toEqual([
      { code: 'BACKPACK', name: 'Name of BACKPACK', count: 5 },
      { code: 'LUGGAGE', name: 'Name of LUGGAGE', count: 0 },
    ]);
  });

  it('carries no count when there was no category facet to read', () => {
    expect(childLinks('BAGS', shop, []).map((link) => link.count)).toEqual([null, null]);
  });
});

describe('nothing moves under the pointer', () => {
  it('keeps an option that reached zero, in its old place', () => {
    const before = [facet('brand', 'Brand', [option('A', 5), option('B', 3), option('C', 1)])];
    const after = [facet('brand', 'Brand', [option('C', 1), option('A', 2)])];

    const kept = keepShape(before, after)[0].options;

    expect(kept.map((item) => [item.value, item.count])).toEqual([
      ['A', 2],
      ['B', 0],
      ['C', 1],
    ]);
  });

  it('adds an option it had not seen at the end', () => {
    const kept = keepShape(
      [facet('brand', 'Brand', [option('A', 5)])],
      [facet('brand', 'Brand', [option('B', 1), option('A', 5)])],
    );
    expect(kept[0].options.map((item) => item.value)).toEqual(['A', 'B']);
  });

  it('does not keep a facet the server dropped: its counts are unknown, not zero', () => {
    const kept = keepShape(
      [facet('brand', 'Brand', [option('A', 5)]), facet('SIZE', 'Axis', [option('SIZE:42', 1)])],
      [facet('SIZE', 'Axis', [option('SIZE:42', 1)])],
    );
    expect(kept.map((item) => item.code)).toEqual(['SIZE']);
  });
});

describe('prices, as a person says them', () => {
  it('drops the cents when there are none', () => {
    expect(sayablePrice(30, 'EUR', 'es').replace(/\s/g, ' ')).toBe('30 €');
    expect(sayablePrice(29.9, 'EUR', 'es').replace(/\s/g, ' ')).toBe('29,90 €');
  });

  it('words an open range at either end', () => {
    const money = (amount: number) => `${amount} €`;
    expect(priceLabel(30, 60, money, words)).toBe('30 € – 60 €');
    expect(priceLabel(null, 30, money, words)).toBe('Up to 30 €');
    expect(priceLabel(100, null, money, words)).toBe('100 € and above');
  });
});

describe('the applied chips', () => {
  const facets = [
    facet('category', 'Category', [option('BAGS', 3, { label: 'Bags' })]),
    facet('brand', 'Brand', [option('Vertex', 2, { label: 'Vertex' })]),
    facet('offer', 'Offer', [option('true', 1)], { label: 'En oferta' }),
    facet('SIZE', 'Axis', [option('SIZE:42', 2, { label: '42' })]),
    facet('COLOR', 'Axis', [option('COLOR:BLACK', 2, { label: 'Negro' })]),
  ];

  it("names every applied filter as the column names it, in the column's order", () => {
    let filters = toggleOption(NoFilters, 'Axis', 'COLOR:BLACK');
    filters = toggleOption(filters, 'Axis', 'SIZE:42');
    filters = toggleOption(filters, 'Brand', 'Vertex');
    filters = toggleOption(filters, 'Category', 'BAGS');
    filters = toggleOption(filters, 'Offer', 'true');
    filters = toggleOption(filters, 'InStock', 'true');
    filters = withPrice(filters, 30, 60);

    const chips = chipsOf(filters, facets, words);

    expect(chips.map((chip) => chip.label)).toEqual([
      'Bags',
      'Vertex',
      '30 € – 60 €',
      'En oferta',
      'In stock',
      '42',
      'Negro',
    ]);
  });

  it('takes off exactly one filter per chip', () => {
    const filters = toggleOption(
      toggleOption(withPrice(NoFilters, 30, null), 'Brand', 'Vertex'),
      'Axis',
      'COLOR:BLACK',
    );

    const chips = chipsOf(filters, facets, words);
    const price = chips.find((chip) => chip.id === 'price');
    const colour = chips.find((chip) => chip.id === 'f:COLOR:BLACK');

    expect(price?.label).toBe('30 € and above');
    expect(price?.without).toEqual({ ...filters, minPrice: null, maxPrice: null });
    expect(colour?.without.values).toEqual([]);
    expect(colour?.without.brands).toEqual(['Vertex']);
  });

  it('still offers a chip for a value no facet names any more, so it can be taken off', () => {
    const chips = chipsOf(
      toggleOption(toggleOption(NoFilters, 'Attribute', 'MATERIAL:WOOL'), 'Brand', 'Gone'),
      [],
      words,
    );
    expect(chips.map((chip) => chip.label)).toEqual(['Gone', 'WOOL']);
  });

  it('has none when nothing is applied', () => {
    expect(chipsOf(NoFilters, facets, words)).toEqual([]);
  });
});

describe('the chip of a campaign listing (R-8)', () => {
  it('comes first, by the campaign name, and takes the campaign off', () => {
    const chips = chipsOf({ ...NoFilters, campaign: 'TENDERO_DAYS', onOffer: true }, [], words);

    expect(chips.map((chip) => chip.label)).toEqual(['Campaign: TENDERO_DAYS', 'On offer']);
    expect(chips[0].without.campaign).toBeNull();
    expect(chips[0].without.onOffer).toBe(true);
  });
});
