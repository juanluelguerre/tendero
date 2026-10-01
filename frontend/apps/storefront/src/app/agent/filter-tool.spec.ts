import type { SearchFacet } from '@tendero/shared-api';
import { describe, expect, it } from 'vitest';
import { NoFilters } from '../listing/listing-filters';
import { chooseFilter, describeFacets } from './filter-tool';

const facets: SearchFacet[] = [
  {
    code: 'brand',
    kind: 'Brand',
    label: 'Marca',
    options: [
      { value: 'Caminante', label: 'Caminante', count: 7, chosen: false },
      { value: 'Vertex', label: 'Vertex', count: 6, chosen: true },
    ],
  },
  {
    code: 'price',
    kind: 'Price',
    label: 'Precio',
    options: [],
    price: { min: 30, max: 160, chosenMin: null, chosenMax: null, bands: [] },
  },
  {
    code: 'offer',
    kind: 'Offer',
    label: 'En oferta',
    options: [{ value: 'true', label: 'En oferta', count: 3, chosen: false }],
  },
  {
    code: 'COLOR',
    kind: 'Axis',
    label: 'Color',
    options: [
      { value: 'COLOR:BLACK', label: 'negro', count: 4, chosen: false },
      { value: 'COLOR:WHITE', label: 'blanco', count: 2, chosen: false },
    ],
  },
  {
    code: 'category',
    kind: 'Category',
    label: 'Categorías',
    options: [{ value: 'BAGS', label: 'Bolsas', count: 5, chosen: false }],
  },
];

const search = { query: 'mochila', category: null };
const shoes = { query: '', category: 'SHOES' };

describe('what list_filters tells an agent', () => {
  it('names each facet by code and each option by the value to send, with its count', () => {
    const text = describeFacets(facets, 13);

    expect(text).toContain('13 product(s)');
    expect(text).toContain('- brand (Marca): Caminante [7], Vertex [6] (chosen)');
    expect(text).toContain('COLOR:BLACK "negro" [4]');
    expect(text).toContain('- price (Precio): from 30 to 160; send minPrice and/or maxPrice');
  });

  it('says so when there is nothing to narrow by', () => {
    expect(describeFacets([], 2)).toBe('2 product(s), and nothing to narrow them by.');
  });
});

describe('what apply_filter does', () => {
  it("replaces a facet's choices with the values sent", () => {
    const outcome = chooseFilter({ ...NoFilters, brands: ['Vertex'] }, facets, shoes, {
      facet: 'brand',
      values: ['Caminante'],
    });

    expect(outcome).toEqual({ ok: true, filters: { ...NoFilters, brands: ['Caminante'] } });
  });

  it("replaces one attribute's pairs and keeps another's", () => {
    const filters = { ...NoFilters, values: ['COLOR:WHITE', 'SIZE:42'] };

    const outcome = chooseFilter(filters, facets, shoes, { facet: 'COLOR', values: ['COLOR:BLACK'] });

    expect(outcome).toEqual({ ok: true, filters: { ...NoFilters, values: ['SIZE:42', 'COLOR:BLACK'] } });
  });

  it('turns a switch on with its value and off with none', () => {
    expect(chooseFilter(NoFilters, facets, shoes, { facet: 'offer', values: ['true'] })).toEqual({
      ok: true,
      filters: { ...NoFilters, onOffer: true },
    });
    expect(chooseFilter({ ...NoFilters, onOffer: true }, facets, shoes, { facet: 'offer', values: [] })).toEqual({
      ok: true,
      filters: NoFilters,
    });
  });

  it('takes a price range, either end open', () => {
    expect(chooseFilter(NoFilters, facets, shoes, { facet: 'price', maxPrice: 60 })).toEqual({
      ok: true,
      filters: { ...NoFilters, maxPrice: 60 },
    });
  });

  it('refuses a facet the list does not offer, naming the ones it does', () => {
    expect(chooseFilter(NoFilters, facets, shoes, { facet: 'SIZE', values: ['SIZE:42'] })).toEqual({
      ok: false,
      reason: 'No facet "SIZE" here. The list offers: brand, price, offer, COLOR, category.',
    });
  });

  it('refuses an option the facet does not have, and a label in place of a value', () => {
    const outcome = chooseFilter(NoFilters, facets, shoes, { facet: 'COLOR', values: ['negro'] });

    expect(outcome).toEqual({
      ok: false,
      reason: 'COLOR has no option "negro". Its options: COLOR:BLACK, COLOR:WHITE.',
    });
  });

  it('refuses a negative price', () => {
    expect(chooseFilter(NoFilters, facets, shoes, { facet: 'price', minPrice: -5 }).ok).toBe(false);
  });

  it('narrows a search by category, and refuses to on a category page, where sections are links', () => {
    expect(chooseFilter(NoFilters, facets, search, { facet: 'category', values: ['BAGS'] })).toEqual({
      ok: true,
      filters: { ...NoFilters, within: ['BAGS'] },
    });
    expect(chooseFilter(NoFilters, facets, shoes, { facet: 'category', values: ['BAGS'] }).ok).toBe(false);
  });
});
