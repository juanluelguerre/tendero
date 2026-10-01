import { describe, expect, it } from 'vitest';
import { NoFilters, withPrice, withSort } from './listing-filters';
import { listingIndexing } from './listing-indexing';

describe('what a listing tells a search engine', () => {
  it('points a filtered category at the category with no filters', () => {
    const filters = { ...withPrice(NoFilters, 30, 60), brands: ['Vertex'] };

    expect(listingIndexing({ query: '', category: 'SHOES' }, filters)).toEqual({
      kind: 'canonical',
      category: 'SHOES',
    });
  });

  it('points a category in another order at the same category', () => {
    expect(listingIndexing({ query: '', category: 'SHOES' }, withSort(NoFilters, 'price-asc'))).toEqual({
      kind: 'canonical',
      category: 'SHOES',
    });
  });

  it('keeps the results of a search out of the index', () => {
    expect(listingIndexing({ query: 'mochila', category: null }, NoFilters)).toEqual({ kind: 'noindex' });
  });

  it('keeps a listing made only of filters out of the index', () => {
    expect(listingIndexing({ query: '', category: null }, { ...NoFilters, onOffer: true })).toEqual({
      kind: 'noindex',
    });
  });

  it('says nothing on the home page, which a sort alone does not change', () => {
    expect(listingIndexing({ query: '', category: null }, withSort(NoFilters, 'newest'))).toEqual({ kind: 'silent' });
  });
});
