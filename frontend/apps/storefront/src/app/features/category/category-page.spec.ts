import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PageTitles } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import type { Category } from '../../data-access/shop-front.service';
import { ListingStore } from '../../listing/listing.store';
import { ShopLinks } from '../../shop-links';
import { CategoryPage } from './category-page';
import { CategoryStore } from './category.store';

/**
 * What a category page does once it knows its section: the tab names it, and
 * `category_opened` is reported once per section, not once per answer. The
 * page's logic only — its template is the browser suite's.
 */
describe('the category page', () => {
  let titled: string[];
  let events: string[];
  const state = signal<'loading' | 'found' | 'unknown' | 'failed'>('loading');
  const category = signal<Category | null>(null);

  const section = (code: string) => ({ code, name: `Sección ${code}` }) as unknown as Category;

  beforeEach(() => {
    titled = [];
    events = [];
    state.set('loading');
    category.set(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PageTitles, useValue: { name: (name: string) => titled.push(name) } },
        { provide: Telemetry, useValue: { trackEvent: (name: string) => events.push(name) } },
        { provide: CampaignsStore, useValue: {} },
        { provide: ShopLinks, useValue: {} },
      ],
    });
    TestBed.overrideComponent(CategoryPage, {
      set: {
        template: '',
        imports: [],
        providers: [
          { provide: CategoryStore, useValue: { state, category } },
          { provide: ListingStore, useValue: { filters: signal({}) } },
        ],
      },
    });
  });

  it('names the tab after its section and reports the visit once, once it knows the section', () => {
    const fixture = TestBed.createComponent(CategoryPage);
    TestBed.tick();
    const whileLoading = [titled.length, events.length];
    category.set(section('BAGS'));
    state.set('found');
    TestBed.tick();
    state.set('loading');
    TestBed.tick();
    state.set('found');
    TestBed.tick();
    fixture.destroy();

    expect([whileLoading, titled, events]).toEqual([[0, 0], ['Sección BAGS', 'Sección BAGS'], ['category_opened']]);
  });
});
