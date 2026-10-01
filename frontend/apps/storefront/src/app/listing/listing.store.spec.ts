import { ApplicationRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { SearchFacet, SearchHit, SearchResultPage } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Subject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { ProductSearchService } from '../data-access/product-search.service';
import { NoFilters } from './listing-filters';
import { ListingPageSize, ListingStore } from './listing.store';

/**
 * One listing's state (ADR 0047): what it asks the search for, and what it
 * keeps on screen while the answer comes. The search answers when the test
 * says, in the order the test says, which is what the store's subtlest rule
 * is about: only the latest question's answer is shown.
 */
describe('the listing store', () => {
  let asked: { params: Record<string, unknown>; answer: Subject<SearchResultPage> }[];
  const culture = signal('es');

  const hit = (code: string) => ({ code, name: code }) as unknown as SearchHit;
  const facet = (field: string) => ({ field, values: [] }) as unknown as SearchFacet;
  const page = (codes: string[], total = codes.length, facets: SearchFacet[] = []): SearchResultPage => ({
    hits: codes.map(hit),
    total,
    page: 1,
    pageSize: ListingPageSize,
    tookMs: 3,
    facets,
  });

  beforeEach(() => {
    asked = [];
    culture.set('es');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ListingStore,
        { provide: CultureStore, useValue: { active: culture } },
        {
          provide: ProductSearchService,
          useValue: {
            list: (params: Record<string, unknown>) => {
              const answer = new Subject<SearchResultPage>();
              asked.push({ params, answer });
              return answer;
            },
          },
        },
      ],
    });
  });

  async function settle() {
    TestBed.tick();
    await TestBed.inject(ApplicationRef).whenStable();
  }

  async function listing(place = { query: '', category: 'BAGS' }) {
    const store = TestBed.inject(ListingStore);
    store.list(place);
    await settle();
    return store;
  }

  function answer(index: number, result: SearchResultPage) {
    asked[index].answer.next(result);
    asked[index].answer.complete();
  }

  it('lists nothing, and asks nothing, while it has no place', async () => {
    const store = TestBed.inject(ListingStore);
    await settle();

    expect([store.result().status, asked.length]).toEqual(['idle', 0]);
  });

  it('asks for the place, loads, then shows the answer', async () => {
    const store = await listing();
    const loading = store.result().status;
    answer(0, page(['A', 'B'], 7, [facet('brand')]));
    await settle();
    const result = store.result();

    expect([
      loading,
      result.status,
      result.status === 'ready' && [result.hits.map((h) => h.code), result.total],
    ]).toEqual(['loading', 'ready', [['A', 'B'], 7]]);
  });

  it('keeps the rows on screen, marked busy, while the same place is narrowed', async () => {
    const store = await listing();
    answer(0, page(['A', 'B']));
    await settle();
    store.apply({ ...NoFilters, inStock: true });
    await settle();
    const result = store.result();

    expect([asked.length, result.status, result.status === 'ready' && result.busy]).toEqual([2, 'ready', true]);
  });

  it('starts from nothing when the place changes', async () => {
    const store = await listing();
    answer(0, page(['A']));
    await settle();
    store.list({ query: '', category: 'SHOES' });
    await settle();

    expect(store.result().status).toBe('loading');
  });

  it('shows only the latest question’s answer, whatever order the answers arrive in', async () => {
    const store = await listing();
    store.list({ query: '', category: 'SHOES' });
    await settle();
    answer(1, page(['SHOE']));
    await settle();
    answer(0, page(['BAG']));
    await settle();
    const result = store.result();

    expect(result.status === 'ready' && result.hits.map((h) => h.code)).toEqual(['SHOE']);
  });

  it('says the list failed when the search does', async () => {
    const store = await listing();
    asked[0].answer.error(new Error('500'));
    await settle();

    expect(store.result().status).toBe('failed');
  });

  it('appends the next page, and keeps what it has when the next page fails', async () => {
    const store = await listing();
    answer(0, page(['A', 'B'], 4));
    await settle();
    store.loadMore();
    answer(1, page(['C', 'D'], 4));
    await settle();
    const appended = store.result();
    store.loadMore();
    asked[2].answer.error(new Error('500'));
    await settle();
    const kept = store.result();

    expect([
      asked[1].params['page'],
      appended.status === 'ready' && appended.hits.map((h) => h.code),
      kept.status === 'ready' && kept.hits.length,
      store.loadingMore(),
    ]).toEqual(['2', ['A', 'B', 'C', 'D'], 4, false]);
  });

  it('asks again in the new language, and says the answer came from the switch', async () => {
    const store = await listing();
    answer(0, page(['A']));
    await settle();
    culture.set('en');
    await settle();
    answer(1, page([]));
    await settle();
    const result = store.result();

    expect([asked.length, result.status === 'ready' && result.afterCultureChange]).toEqual([2, true]);
  });

  it('answers a choice made by the browser agent with the list it leads to', async () => {
    const store = await listing();
    answer(0, page(['A', 'B']));
    await settle();
    const settled = store.applyAndSettle({ ...NoFilters, onOffer: true });
    await settle();
    answer(1, page(['B']));
    const result = await settled;

    expect(result.status === 'ready' && result.hits.map((h) => h.code)).toEqual(['B']);
  });
});
