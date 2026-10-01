import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import type { SearchFacet, SearchHit, SearchResultPage } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../campaigns/campaigns.store';
import { ProductSearchService } from '../data-access/product-search.service';
import { TaxonomyStore } from '../taxonomy/taxonomy.store';
import { Listing } from './listing';
import { ListingStore } from './listing.store';

/** A page that lists one category, as the category page does once it knows the code. */
@Component({
  selector: 'storefront-listing-host',
  imports: [Listing],
  providers: [ListingStore],
  template: '<storefront-listing />',
})
class ListingHost {
  constructor() {
    inject(ListingStore).list({ query: '', category: 'SHOES' });
  }
}

/**
 * The listing's wiring, which the pure functions cannot prove: a choice in the
 * column becomes the URL, the URL becomes the request, and a chip takes its
 * filter off again. The browser half (the sheet's focus, the widths) is
 * `storefront-e2e/src/listing-filters.spec.ts`.
 */
describe('the listing', () => {
  let asked: Record<string, string | string[]>[];
  let events: [string, unknown][];
  let http: HttpTestingController;

  beforeEach(() => {
    asked = [];
    events = [];
    try {
      localStorage.clear();
    } catch {
      // No storage: the fold is remembered for the visit only, which is what the test needs anyway.
    }

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideOAuthClient(),
        provideTenderoI18n(),
        provideRouter([{ path: '', component: ListingHost }]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        // The campaigns are a root resource; answered as none, so nothing waits on them.
        { provide: CampaignsStore, useValue: { named: () => null, all: () => [], leading: () => null } },
        {
          provide: Telemetry,
          useValue: {
            trackEvent: (name: string, attributes: unknown) => events.push([name, attributes]),
          } as unknown as Telemetry,
        },
        { provide: TaxonomyStore, useValue: { categories: () => [], menu: () => [] } },
        {
          provide: ProductSearchService,
          useValue: {
            list: (params: Record<string, string | string[]>) => {
              asked.push(params);
              return of(page(params));
            },
          } as unknown as ProductSearchService,
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function open(url = '/'): Promise<{ harness: RouterTestingHarness; element: HTMLElement }> {
    const harness = await RouterTestingHarness.create(url);
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush(TRANSLATIONS);
    await settle(harness);
    const element = harness.routeNativeElement;
    if (!element) throw new Error('The listing did not render.');
    return { harness, element };
  }

  async function settle(harness: RouterTestingHarness): Promise<void> {
    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  /** The latest request the listing made. */
  function last(): Record<string, string | string[]> | undefined {
    return asked[asked.length - 1];
  }

  function url(): URLSearchParams {
    return new URLSearchParams(TestBed.inject(Router).url.split('?')[1] ?? '');
  }

  function column(element: HTMLElement): HTMLElement {
    const aside = element.querySelector<HTMLElement>('#listing-filters');
    if (!aside) throw new Error('No column.');
    return aside;
  }

  it('asks for the category with no filters, and draws each filter as a named group', async () => {
    const { element } = await open();

    expect(asked[0]).toEqual({ culture: expect.any(String), pageSize: '24', category: 'SHOES' });

    const groups = [...column(element).querySelectorAll('[role="group"]')].map((group) =>
      document.getElementById(group.getAttribute('aria-labelledby') ?? '')?.textContent?.trim(),
    );
    expect(groups).toEqual(['Marca', 'Precio', 'Offers', 'Color']);
    expect(element.querySelectorAll('storefront-product-card')).toHaveLength(2);
  });

  it('puts a brand in the URL and the request, shows its chip, and the chip takes it off', async () => {
    const { harness, element } = await open();

    const vertex = [...column(element).querySelectorAll<HTMLLabelElement>('label.check')].find((label) =>
      label.textContent?.includes('Vertex'),
    );
    // The count is part of the name a screen reader reads.
    expect(vertex?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Vertex (2)');
    vertex?.querySelector('input')?.click();
    await settle(harness);

    expect(url().getAll('brand')).toEqual(['Vertex']);
    expect(last()?.['brand']).toEqual(['Vertex']);

    const chip = element.querySelector<HTMLButtonElement>('.chips .chip');
    expect(chip?.textContent?.trim()).toBe('Vertex');
    chip?.click();
    await settle(harness);

    expect(url().getAll('brand')).toEqual([]);
    expect(element.querySelector('.chips')).toBeNull();

    // F-6: the choice is counted once, by facet and never by value; taking it off is not a choice.
    expect(events).toEqual([['filter_applied', { facet: 'brand', listing: 'category', surface: 'column' }]]);
  });

  it('fills the range from a band, and the URL holds the range, never the band', async () => {
    const { harness, element } = await open();

    column(element).querySelector<HTMLInputElement>('input[type="radio"]')?.click();
    await settle(harness);

    expect(url().get('price')).toBe('30-60');
    expect(last()).toMatchObject({ minPrice: '30', maxPrice: '60' });
    const boxes = [...column(element).querySelectorAll<HTMLInputElement>('.range__input')].map((box) => box.value);
    expect(boxes).toEqual(['30', '60']);
  });

  it('draws a switch as one, with its state, and a colour with its name beside the swatch', async () => {
    const { harness, element } = await open('/?offer=1');

    const offer = column(element).querySelector<HTMLButtonElement>('[role="switch"]');
    expect(offer?.getAttribute('aria-checked')).toBe('true');
    expect(last()?.['onOffer']).toBe('true');

    const black = column(element).querySelector<HTMLButtonElement>('.swatch');
    expect(black?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Negro (1)');
    expect(black?.querySelector<HTMLElement>('.swatch__dot')?.style.backgroundColor).not.toBe('');
    // A colour nothing is left in is disabled, not hidden.
    const red = [...column(element).querySelectorAll<HTMLButtonElement>('.swatch')].find((button) =>
      button.textContent?.includes('Rojo'),
    );
    expect(red?.disabled).toBe(true);

    black?.click();
    await settle(harness);
    expect(url().getAll('f')).toEqual(['COLOR:BLACK']);
  });

  it('folds the column and remembers it', async () => {
    const { harness, element } = await open();

    const fold = element.querySelector<HTMLButtonElement>('.toolbar__fold');
    expect(fold?.getAttribute('aria-expanded')).toBe('true');
    fold?.click();
    await settle(harness);

    expect(element.querySelector('.listing')?.classList).toContain('listing--folded');
    expect(fold?.getAttribute('aria-expanded')).toBe('false');
    expect(localStorage.getItem('tendero.listing.filtersFolded')).toBe('1');
  });

  it("collects the sheet's choices as a batch and applies them as one navigation", async () => {
    const { harness, element } = await open();
    const dialog = element.querySelector('dialog');
    // jsdom has no modal dialogs; the platform's part is the browser's to prove.
    if (dialog) dialog.showModal = () => dialog.setAttribute('open', '');
    if (dialog)
      dialog.close = () => {
        dialog.removeAttribute('open');
        dialog.dispatchEvent(new Event('close'));
      };

    element.querySelector<HTMLButtonElement>('.toolbar__filters')?.click();
    await settle(harness);

    const sheet = element.querySelector<HTMLElement>('.sheet__body');
    [...(sheet?.querySelectorAll<HTMLLabelElement>('label.check') ?? [])]
      .find((label) => label.textContent?.includes('Caminante'))
      ?.querySelector('input')
      ?.click();
    await settle(harness);

    // Nothing applied yet: the page does not reload under a thumb.
    expect(url().getAll('brand')).toEqual([]);

    element.querySelector<HTMLButtonElement>('.sheet__apply')?.click();
    await settle(harness);

    expect(url().getAll('brand')).toEqual(['Caminante']);
    expect(dialog?.hasAttribute('open')).toBe(false);
    // Counted when the batch applies, not on the tap.
    expect(events).toEqual([['filter_applied', { facet: 'brand', listing: 'category', surface: 'sheet' }]]);
  });
});

const TRANSLATIONS = {
  listing: {
    section: { switches: 'Offers' },
    clearAll: 'Clear all',
  },
};

const hit = (id: string): SearchHit => ({
  productId: id,
  code: id.toUpperCase(),
  name: `Product ${id}`,
  slug: `product-${id}`,
  brand: 'Vertex',
  category: null,
  matchedVariantId: `${id}-DEFAULT`,
  matchedSku: `${id}-DEFAULT`,
  priceAmount: 49.9,
  priceFrom: 49.9,
  priceTo: 49.9,
  priceCurrency: 'EUR',
  imageId: null,
  inStock: true,
  score: 1,
});

/** The facets a shoe category answers with; `chosen` follows what was asked, as the API's does. */
function page(params: Record<string, string | string[]>): SearchResultPage {
  const has = (name: string, value: string) => ([] as string[]).concat(params[name] ?? []).includes(value);
  const facets: SearchFacet[] = [
    {
      code: 'brand',
      kind: 'Brand',
      label: 'Marca',
      options: [
        { value: 'Vertex', label: 'Vertex', count: 2, chosen: has('brand', 'Vertex') },
        { value: 'Caminante', label: 'Caminante', count: 1, chosen: has('brand', 'Caminante') },
      ],
    },
    {
      code: 'price',
      kind: 'Price',
      label: 'Precio',
      options: [],
      price: {
        min: 29.9,
        max: 169,
        chosenMin: null,
        chosenMax: null,
        bands: [
          { from: 30, to: 60, count: 1, chosen: false },
          { from: 60, to: null, count: 1, chosen: false },
        ],
      },
    },
    {
      code: 'offer',
      kind: 'Offer',
      label: 'En oferta',
      options: [{ value: 'true', label: 'En oferta', count: 1, chosen: has('onOffer', 'true') }],
    },
    {
      code: 'COLOR',
      kind: 'Axis',
      label: 'Color',
      options: [
        { value: 'COLOR:BLACK', label: 'Negro', count: 1, chosen: has('f', 'COLOR:BLACK'), swatch: '#201D1B' },
        { value: 'COLOR:RED', label: 'Rojo', count: 0, chosen: false, swatch: '#B03A2E' },
      ],
    },
  ];

  return { hits: [hit('a'), hit('b')], total: 2, page: 1, pageSize: 24, tookMs: 3, facets };
}
