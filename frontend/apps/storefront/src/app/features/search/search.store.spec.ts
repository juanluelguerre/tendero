import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import type { CampaignView } from '@tendero/shared-api';
import { BehaviorSubject } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { parseListing, type ListingFilters, type ListingPlace } from '../../listing/listing-filters';
import type { ListingResult } from '../../listing/listing.store';
import { ListingStore } from '../../listing/listing.store';
import { TrailStore } from '../../trail/trail.store';
import { SearchStore } from './search.store';

const EmptyFilters = parseListing(convertToParamMap({}));

const ready = (busy = false): ListingResult =>
  ({
    status: 'ready',
    hits: [],
    total: 0,
    facets: [],
    tookMs: 1,
    answer: null,
    afterCultureChange: false,
    busy,
  }) as ListingResult;

/** The home is the search (ADR 0047): what the URL asks for, what the listing lists, what the trail remembers. */
describe('the search store', () => {
  const query = new BehaviorSubject(convertToParamMap({}));
  const filters = signal<ListingFilters>(EmptyFilters);
  const result = signal<ListingResult>({ status: 'idle' });
  let listed: (ListingPlace | null)[];
  let remembered: string[];

  beforeEach(() => {
    query.next(convertToParamMap({}));
    filters.set(EmptyFilters);
    result.set({ status: 'idle' });
    listed = [];
    remembered = [];
    TestBed.configureTestingModule({
      providers: [
        SearchStore,
        { provide: ActivatedRoute, useValue: { queryParamMap: query } },
        {
          provide: ListingStore,
          useValue: { list: (place: ListingPlace | null) => listed.push(place), filters, result },
        },
        { provide: TrailStore, useValue: { recordSearch: (text: string) => remembered.push(text) } },
        {
          provide: CampaignsStore,
          useValue: { named: (code: string) => (code === 'OTONO' ? ({ code, name: 'Otoño' } as CampaignView) : null) },
        },
      ],
    });
  });

  function opened() {
    const store = TestBed.inject(SearchStore);
    TestBed.tick();
    return store;
  }

  it('is the front of the shop with no words and no filter, and lists nothing', () => {
    const store = opened();

    expect([store.mode(), listed]).toEqual(['idle', [null]]);
  });

  /** A single character is not a search: the box says so instead of sending it. */
  it('does not send a query of one character', () => {
    query.next(convertToParamMap({ q: ' a ' }));

    expect([opened().mode(), listed]).toEqual(['tooShort', [null]]);
  });

  it('lists the words the URL carries, and a filter alone lists too', () => {
    query.next(convertToParamMap({ q: ' mochila ' }));
    const store = opened();
    query.next(convertToParamMap({}));
    filters.set({ ...EmptyFilters, onOffer: true });
    TestBed.tick();

    expect([store.mode(), listed]).toEqual([
      'listing',
      [
        { query: 'mochila', category: null },
        { query: '', category: null },
      ],
    ]);
  });

  /** *Sigue donde lo dejaste*: words that were answered, once — not every keystroke, and not while a newer answer is on its way. */
  it('remembers a search once it has been answered', () => {
    query.next(convertToParamMap({ q: 'mochila' }));
    opened();
    result.set(ready(true));
    TestBed.tick();
    result.set(ready());
    TestBed.tick();
    result.set({ ...ready(), total: 3 } as ListingResult);
    TestBed.tick();

    expect(remembered).toEqual(['mochila']);
  });

  it('names the campaign a listing is narrowed to', () => {
    filters.set({ ...EmptyFilters, campaign: 'OTONO' });

    expect(opened().campaign()?.name).toBe('Otoño');
  });
});
