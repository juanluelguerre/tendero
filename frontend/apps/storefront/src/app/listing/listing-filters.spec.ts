import { describe, expect, it } from 'vitest';
import {
  appliedCount,
  clearAll,
  isChosen,
  listingQueryParams,
  narrowedFacets,
  NoFilters,
  parseAmount,
  parseListing,
  sameFilters,
  searchParams,
  toggleOption,
  withPrice,
  withSort,
  type ListingFilters,
  type ReadableParams,
} from './listing-filters';

/** A query string as `ParamMap` reads it: `getAll` for the repeated parameters. */
function url(query: string): ReadableParams {
  const params = new URLSearchParams(query);
  return { get: (name) => params.get(name), getAll: (name) => params.getAll(name) };
}

/** What a navigation with these filters would leave in the address bar. */
function addressOf(filters: ListingFilters): string {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(listingQueryParams(filters))) {
    if (value === null) continue;
    for (const item of Array.isArray(value) ? value : [value]) params.append(name, String(item));
  }
  return params.toString();
}

describe('the listing in the URL', () => {
  it('reads every parameter it writes', () => {
    const filters: ListingFilters = {
      brands: ['Vertex', 'Caminante'],
      within: ['BAGS'],
      values: ['COLOR:BLACK', 'SIZE:42'],
      minPrice: 30,
      maxPrice: 59.9,
      onOffer: true,
      newArrivals: true,
      inStock: true,
      sort: 'price-asc',
      campaign: 'TENDERO_DAYS',
    };

    const address = addressOf(filters);

    expect(address).toBe(
      'brand=Vertex&brand=Caminante&within=BAGS&f=COLOR%3ABLACK&f=SIZE%3A42&price=30-59.9&offer=1&new=1&stock=1&sort=price-asc&campaign=TENDERO_DAYS',
    );
    expect(parseListing(url(address))).toEqual(filters);
  });

  it('writes nothing for a listing with no filters, and null removes every parameter on a merge', () => {
    expect(addressOf(NoFilters)).toBe('');
    expect(Object.values(listingQueryParams(NoFilters)).every((value) => value === null)).toBe(true);
  });

  it('keeps a value with a comma whole, which a comma list could not', () => {
    const filters = parseListing(url('f=CAPACITY%3A1%2C5+L&brand=Lumen%26Co'));
    expect(filters.values).toEqual(['CAPACITY:1,5 L']);
    expect(filters.brands).toEqual(['Lumen&Co']);
  });

  it('reads an open range at either end', () => {
    expect(parseListing(url('price=30-'))).toMatchObject({ minPrice: 30, maxPrice: null });
    expect(parseListing(url('price=-60'))).toMatchObject({ minPrice: null, maxPrice: 60 });
    expect(addressOf(withPrice(NoFilters, null, 60))).toBe('price=-60');
  });

  it('ignores what it cannot use rather than asking the API for it', () => {
    const filters = parseListing(url('price=cheap&sort=random&f=NOCODE&offer=yes&brand=&brand=Vertex&brand=Vertex'));
    expect(filters).toEqual({ ...NoFilters, brands: ['Vertex'] });
  });

  it('puts a reversed range the right way round', () => {
    expect(parseListing(url('price=80-30'))).toMatchObject({ minPrice: 30, maxPrice: 80 });
  });

  it('takes "true" as well as "1" for a switch, so a hand-written link works', () => {
    expect(parseListing(url('offer=true')).onOffer).toBe(true);
  });
});

describe('the API parameters', () => {
  const filters: ListingFilters = {
    ...NoFilters,
    brands: ['Vertex'],
    within: ['BAGS'],
    values: ['COLOR:BLACK'],
    minPrice: 30,
    maxPrice: null,
    onOffer: true,
    inStock: true,
    sort: 'newest',
  };

  it('sends the search its filters, arrays repeated and the switches as booleans', () => {
    expect(searchParams({ query: 'mochila', category: null }, filters, 'es', 1, 24)).toEqual({
      culture: 'es',
      pageSize: '24',
      q: 'mochila',
      // The URL's word, sent as the API's enum.
      sort: 'Newest',
      brand: ['Vertex'],
      within: ['BAGS'],
      f: ['COLOR:BLACK'],
      // The address says 30 as a person types it; the API is asked in the
      // unit it answers prices in.
      minPrice: '3000',
      onOffer: 'true',
      inStock: 'true',
    });
  });

  it('asks for a price in minor units, whole, whatever a person typed', () => {
    const typed = { ...NoFilters, minPrice: 27.5, maxPrice: 59.9 };

    expect(searchParams({ query: '', category: 'HOME' }, typed, 'es', 1, 24)).toMatchObject({
      minPrice: '2750',
      maxPrice: '5990',
    });
  });

  it('does not send `within` on a category page, where the category is the place', () => {
    const params = searchParams({ query: '', category: 'BAGS' }, filters, 'en', 2, 24);
    expect(params['category']).toBe('BAGS');
    expect(params['within']).toBeUndefined();
    expect(params['q']).toBeUndefined();
    expect(params['page']).toBe('2');
  });

  it('sends only the culture and the page size for a bare listing', () => {
    expect(searchParams({ query: '', category: null }, NoFilters, 'es', 1, 24)).toEqual({
      culture: 'es',
      pageSize: '24',
    });
  });
});

describe('changing the filters', () => {
  it('toggles an option of every kind on and off again', () => {
    const cases: [Parameters<typeof toggleOption>[1], string][] = [
      ['Category', 'BAGS'],
      ['Brand', 'Vertex'],
      ['Axis', 'COLOR:BLACK'],
      ['Attribute', 'MATERIAL:LEATHER'],
      ['Flag', 'INDUCTION:true'],
      ['Offer', 'true'],
      ['New', 'true'],
      ['InStock', 'true'],
    ];

    for (const [kind, value] of cases) {
      const on = toggleOption(NoFilters, kind, value);
      expect(isChosen(on, kind, value), `${kind} on`).toBe(true);
      expect(appliedCount(on), `${kind} counted`).toBe(1);
      expect(sameFilters(toggleOption(on, kind, value), NoFilters), `${kind} off`).toBe(true);
    }
  });

  it('adds a second brand to the first (OR within a facet)', () => {
    const two = toggleOption(toggleOption(NoFilters, 'Brand', 'Vertex'), 'Brand', 'Caminante');
    expect(two.brands).toEqual(['Vertex', 'Caminante']);
    expect(appliedCount(two)).toBe(2);
  });

  it('counts a price range as one filter, however many ends it has', () => {
    expect(appliedCount(withPrice(NoFilters, 30, 60))).toBe(1);
    expect(appliedCount(withPrice(NoFilters, null, 60))).toBe(1);
  });

  it('refuses a negative price and puts reversed ends right', () => {
    expect(withPrice(NoFilters, -5, 20)).toMatchObject({ minPrice: null, maxPrice: 20 });
    expect(withPrice(NoFilters, 60, 30)).toMatchObject({ minPrice: 30, maxPrice: 60 });
  });

  it('clears every filter and keeps the order, which is not one', () => {
    const busy = withSort(toggleOption(withPrice(NoFilters, 30, 60), 'Brand', 'Vertex'), 'price-desc');
    expect(clearAll(busy)).toEqual({ ...NoFilters, sort: 'price-desc' });
  });

  it('asks the same question whatever order the choices were made in', () => {
    const one = { ...NoFilters, brands: ['A', 'B'] };
    const other = { ...NoFilters, brands: ['B', 'A'] };
    expect(sameFilters(one, other)).toBe(true);
    expect(sameFilters(one, { ...other, inStock: true })).toBe(false);
  });

  it('reads a typed price with a comma or a point, and nothing as nothing', () => {
    expect(parseAmount('29,90')).toBe(29.9);
    expect(parseAmount(' 30 ')).toBe(30);
    expect(parseAmount('')).toBeNull();
    expect(parseAmount('abc')).toBeNull();
    expect(parseAmount('-3')).toBeNull();
  });
});

describe('which facets a change narrowed, for filter_applied', () => {
  it('names each facet a choice was added to or moved in, by its code', () => {
    const before = { ...NoFilters, brands: ['Vertex'], values: ['SIZE:42'] };
    const after = {
      ...NoFilters,
      brands: ['Vertex', 'Caminante'],
      values: ['SIZE:42', 'COLOR:BLACK'],
      maxPrice: 60,
      inStock: true,
    };

    expect(narrowedFacets(before, after)).toEqual(['brand', 'price', 'stock', 'COLOR']);
  });

  it('counts a choice replaced within an attribute, and not one left as it was', () => {
    expect(
      narrowedFacets(
        { ...NoFilters, values: ['COLOR:WHITE', 'SIZE:42'] },
        { ...NoFilters, values: ['COLOR:BLACK', 'SIZE:42'] },
      ),
    ).toEqual(['COLOR']);
  });

  it('does not count a facet taken off, nor an order', () => {
    expect(
      narrowedFacets({ ...NoFilters, brands: ['Vertex'], onOffer: true }, { ...NoFilters, sort: 'newest' }),
    ).toEqual([]);
  });
});

describe('a campaign listing (R-8)', () => {
  it('reads a campaign code, and nothing that is not one', () => {
    expect(parseListing(url('campaign=tendero_days')).campaign).toBe('TENDERO_DAYS');
    expect(parseListing(url('campaign=')).campaign).toBeNull();
    expect(parseListing(url('campaign=%3Cscript%3E')).campaign).toBeNull();
  });

  it('asks the API for what the campaign reaches, and counts as a filter', () => {
    const filters = { ...NoFilters, campaign: 'TENDERO_DAYS' };

    expect(searchParams({ query: '', category: null }, filters, 'es', 1, 24)).toEqual({
      culture: 'es',
      pageSize: '24',
      campaign: 'TENDERO_DAYS',
    });
    expect(appliedCount(filters)).toBe(1);
    expect(sameFilters(filters, NoFilters)).toBe(false);
    expect(clearAll(filters).campaign).toBeNull();
  });
});
