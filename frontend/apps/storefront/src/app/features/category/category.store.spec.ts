import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import type { CampaignView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { of, throwError, type Observable } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ShopFrontService, type Category } from '../../data-access/shop-front.service';
import type { ListingPlace } from '../../listing/listing-filters';
import { ListingStore } from '../../listing/listing.store';
import { CategoryStore } from './category.store';
import { signalMethod } from '@ngrx/signals';

const category = (code: string, parent: string | null, depth: number): Category =>
  ({ code, parent, name: code, path: [], depth }) as unknown as Category;

const taxonomy = [
  category('HOME', null, 0),
  category('KITCHEN', 'HOME', 1),
  category('TEXTILES', 'HOME', 1),
  category('BAGS', null, 0),
];

/** A category page's own state (ADR 0047): the branch the URL names, and what it tells the listing to list. */
describe('the category store', () => {
  let answer: () => Observable<{ items: Category[] }>;
  let listed: (ListingPlace | null)[];
  let code: string;
  const campaigns = signal<readonly CampaignView[]>([]);

  beforeEach(() => {
    answer = () => of({ items: taxonomy });
    listed = [];
    code = 'home';
    campaigns.set([]);
  });

  function opened() {
    TestBed.configureTestingModule({
      providers: [
        CategoryStore,
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ code })) } },
        { provide: CultureStore, useValue: { active: signal('es') } },
        { provide: ShopFrontService, useValue: { categories: () => answer() } },
        { provide: CampaignsStore, useValue: { all: campaigns } },
        {
          provide: ListingStore,
          // A factory, so the signalMethod is made where it can follow a signal: in an injection context.
          useFactory: () => ({
            // As the real one is: a signalMethod, following the signal the page hands it.
            list: signalMethod<ListingPlace | null>((place) => listed.push(place)),
            result: signal({ status: 'idle' }),
          }),
        },
      ],
    });
    const store = TestBed.inject(CategoryStore);
    TestBed.tick();
    return store;
  }

  it('finds the category the URL names, in any case, with its sections, and lists its branch', () => {
    const store = opened();

    expect([store.state(), store.category()?.code, store.children().map((child) => child.code), listed]).toEqual([
      'found',
      'HOME',
      ['KITCHEN', 'TEXTILES'],
      [{ query: '', category: 'HOME' }],
    ]);
  });

  /** "No products here" and "no such category" are different answers, and only one is the shopper's fault. */
  it('says a code nobody recognises is unknown, and lists nothing', () => {
    code = 'NOWHERE';
    const store = opened();

    expect([store.state(), listed]).toEqual(['unknown', [null]]);
  });

  it('says the taxonomy failed rather than that the category is unknown', () => {
    answer = () => throwError(() => new Error('down'));

    expect(opened().state()).toBe('failed');
  });

  /** *Semana del hogar* on Hogar is the banner of everything under it too (R-8). */
  it('heads a section with the campaign of its branch', () => {
    code = 'KITCHEN';
    campaigns.set([{ code: 'HOME_WEEK', scope: 'Category', categoryCode: 'HOME', phase: 'Running' } as CampaignView]);

    expect(opened().banner()?.code).toBe('HOME_WEEK');
  });
});
