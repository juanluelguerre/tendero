import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { CampaignView, RankedProducts, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ShopFrontService } from '../../data-access/shop-front.service';
import { WhatSellsService } from '../../data-access/what-sells.service';
import { TaxonomyStore } from '../../taxonomy/taxonomy.store';
import { EmptyTrail, type TrailSnapshot } from '../../trail/trail-rules';
import { TrailStore } from '../../trail/trail.store';
import { HomeStore } from './home.store';

const hit = (id: string): SearchHit => ({ productId: id, code: id.toUpperCase(), name: id }) as unknown as SearchHit;
const ranked = (): RankedProducts => ({ items: [{ productId: 'a', rank: 1 }] }) as unknown as RankedProducts;

/** The front of the shop's reads (R-8; ADR 0047): each band is its own request, and fails alone. */
describe('the home store', () => {
  let failing: Set<string>;
  let asked: string[];
  const leading = signal<CampaignView | null>(null);
  const shown = signal<TrailSnapshot>(EmptyTrail);

  const answer = <T>(band: string, value: T): Observable<T> => {
    asked.push(band);
    return failing.has(band) ? throwError(() => new Error(`${band} is down`)) : of(value);
  };

  beforeEach(() => {
    failing = new Set();
    asked = [];
    leading.set(null);
    shown.set(EmptyTrail);
    TestBed.configureTestingModule({
      providers: [
        HomeStore,
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: CampaignsStore, useValue: { leading } },
        { provide: TrailStore, useValue: { shown } },
        { provide: TaxonomyStore, useValue: { categories: () => [] } },
        { provide: ShopFrontService, useValue: { offers: () => answer('rules', { items: [] }) } },
        {
          provide: WhatSellsService,
          useValue: {
            trending: () => answer('trending', ranked()),
            bestSellers: () => answer('bestSellers', ranked()),
          },
        },
        {
          provide: ProductSearchService,
          useValue: {
            browse: (options: { onOffer?: boolean; campaign?: string }) =>
              answer(options.onOffer ? 'offers' : options.campaign ? 'campaignProducts' : 'newest', {
                hits: [hit('a')],
                total: 1,
              }),
            cards: () => answer('cards', { items: [hit('a')] }),
            similar: () => answer('similar', { items: [], rankedBy: 'Rule' }),
          },
        },
      ],
    });
  });

  function opened() {
    const store = TestBed.inject(HomeStore);
    TestBed.tick();
    return store;
  }

  it('loses only the band whose request failed', () => {
    failing.add('offers');
    const store = opened();

    expect([store.offerHits(), store.newestHits().map((each) => each.code), store.bestSellers().length]).toEqual([
      [],
      ['A'],
      1,
    ]);
  });

  /** *Puede que te guste* is about what was viewed: a first visit asks for nothing it cannot use. */
  it('asks for suggestions only once there is a trail to suggest from', () => {
    const store = opened();
    const before = asked.includes('similar');
    shown.set({ ...EmptyTrail, views: [{ key: 'A', at: '2026-09-29T10:00:00Z' }] });
    TestBed.tick();

    expect([before, asked.includes('similar'), store.viewed().map((each) => each.code)]).toEqual([false, true, ['A']]);
  });

  /** The leading campaign's card shows its products only when it has no picture of its own, and only while it runs. */
  it('reads the leading campaign’s products only when its card has no picture', () => {
    leading.set({ code: 'OTONO', phase: 'Running', imageId: 'picture' } as CampaignView);
    opened();
    const withPicture = asked.includes('campaignProducts');
    leading.set({ code: 'OTONO', phase: 'Running', imageId: null } as CampaignView);
    TestBed.tick();

    expect([withPicture, asked.includes('campaignProducts')]).toEqual([false, true]);
  });
});
