import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { DeferBlockBehavior, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import type { CampaignView, RankedProducts, SearchHit, SearchResultPage } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ReturnsPolicyStore } from '../../returns/returns-policy.store';
import { ShopFrontService } from '../../data-access/shop-front.service';
import { TaxonomyStore } from '../../taxonomy/taxonomy.store';
import { WhatSellsService } from '../../data-access/what-sells.service';
import { EmptyTrail } from '../../trail/trail-rules';
import { TrailStore } from '../../trail/trail.store';
import { HomeSections } from './home-sections';

/**
 * The home's promise that **each band fails alone** (R-8): a request that fails,
 * or answers nothing, takes its own band away and nothing else. The bands'
 * cards are deferred to the viewport, so these read the bands themselves.
 */
describe('the home page', () => {
  let failing: Set<string>;
  let campaigns: CampaignView[];
  let events: [string, unknown][];

  const answer = <T>(band: string, value: T): Observable<T> =>
    failing.has(band) ? throwError(() => new Error(`${band} is down`)) : of(value);

  beforeEach(() => {
    failing = new Set();
    events = [];
    campaigns = [campaign()];

    TestBed.configureTestingModule({
      deferBlockBehavior: DeferBlockBehavior.Manual,
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideOAuthClient(),
        provideTenderoI18n(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        {
          provide: Telemetry,
          useValue: { trackEvent: (n: string, a: unknown) => events.push([n, a]) } as unknown as Telemetry,
        },
        {
          provide: CampaignsStore,
          useValue: { leading: () => campaigns[0] ?? null, all: () => campaigns, refresh: () => undefined },
        },
        {
          provide: TrailStore,
          useValue: { any: signal(false), shown: signal(EmptyTrail), kept: signal(EmptyTrail) },
        },
        { provide: ReturnsPolicyStore, useValue: { windowDays: () => 14 } },
        {
          provide: TaxonomyStore,
          useValue: {
            categories: () =>
              failing.has('departments')
                ? []
                : [{ code: 'HOME', parent: null, name: 'Hogar', path: ['Hogar'], depth: 0 }],
          },
        },
        {
          provide: ShopFrontService,
          useValue: {
            offers: () =>
              answer('rules', {
                items: [
                  { code: 'SHIP0', name: 'Envío gratis desde 50 €', kind: 'free-shipping', amount: null, until: null },
                ],
              }),
          } as unknown as ShopFrontService,
        },
        {
          provide: WhatSellsService,
          useValue: {
            trending: () => answer<RankedProducts>('trending', ranked()),
            bestSellers: () => answer<RankedProducts>('bestSellers', ranked()),
          } as unknown as WhatSellsService,
        },
        {
          provide: ProductSearchService,
          useValue: {
            browse: (options: { onOffer?: boolean; campaign?: string }) =>
              answer(
                options.onOffer ? 'offers' : options.campaign ? 'campaignProducts' : 'newest',
                page([hit('a'), hit('b')]),
              ),
            cards: () => of({ items: [hit('a'), hit('b')] }),
            similar: () => of({ items: [], rankedBy: 'Rule' }),
          } as unknown as ProductSearchService,
        },
      ],
    });
  });

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(HomeSections);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush(TRANSLATIONS);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const headings = (element: HTMLElement) => [...element.querySelectorAll('h2')].map((h) => h.textContent?.trim());

  it('draws every band, in the spec order, when everything answers', async () => {
    const element = await render();

    expect(headings(element)).toEqual([
      'Tendero Days',
      'Compra con tu agente',
      'Ofertas',
      'Tendencias',
      'Lo más vendido',
      'Novedades',
      'Secciones de la tienda',
    ]);
    expect(element.querySelector('storefront-promise-strip')?.textContent).toContain('Envío gratis desde 50 €');
  });

  it('loses only the bands whose requests failed', async () => {
    failing = new Set(['offers', 'trending', 'bestSellers']);

    const element = await render();

    expect(headings(element)).toEqual(['Tendero Days', 'Compra con tu agente', 'Novedades', 'Secciones de la tienda']);
  });

  it('starts with the next band when no campaign is near, and draws no empty trail', async () => {
    campaigns = [];

    const element = await render();

    expect(element.querySelector('storefront-campaign-card')).toBeNull();
    expect(element.querySelector('storefront-trail-row')).toBeNull();
    expect(headings(element)[0]).toBe('Compra con tu agente');
  });

  it('keeps the promise it can back, and drops the shipping line when the rules are down', async () => {
    failing = new Set(['rules']);

    const element = await render();

    expect(element.querySelector('storefront-promise-strip')?.textContent).not.toContain('Envío gratis');
    expect(element.querySelector('storefront-promise-strip')?.textContent).toContain('14');
  });

  it('reports which module was opened, and where it stood', async () => {
    const element = await render();

    // With Ctrl held the router leaves the page alone (a new tab's gesture), and the
    // browser's own default is cancelled at the document: the click still counts.
    element.ownerDocument.addEventListener('click', (click) => click.preventDefault(), { once: true });
    element
      .querySelector('[storefrontHomeModule="departments"] a')
      ?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));

    // Campaign 1, agent 2, offers 3, trending 4, best sellers 5, new 6, departments 7.
    expect(events).toContainEqual(['home_module_opened', { module: 'departments', position: 7 }]);
  });
});

const TRANSLATIONS = {
  home: {
    offers: 'Ofertas',
    trending: 'Tendencias',
    bestSellers: 'Lo más vendido',
    newest: 'Novedades',
    departments: 'Secciones de la tienda',
    trail: 'Sigue donde lo dejaste',
    suggested: 'Puede que te guste',
  },
  agentCard: { title: 'Compra con tu agente' },
  promise: { returns: '{{days}} días para devolver' },
};

function ranked(): RankedProducts {
  return {
    days: 7,
    items: [
      { rank: 1, productId: 'a' },
      { rank: 2, productId: 'b' },
    ],
  };
}

function campaign(): CampaignView {
  return {
    code: 'TENDERO_DAYS',
    name: 'Tendero Days',
    line: 'Una semana de precios de tienda de barrio',
    kind: 'Event',
    scope: 'Shop',
    categoryCode: null,
    phase: 'Running',
    startsAt: '2026-10-01T00:00:00+02:00',
    endsAt: '2099-10-07T23:59:00+02:00',
    priority: 100,
    holds: [],
    imageId: null,
    imageAlt: null,
  };
}

function hit(id: string): SearchHit {
  return {
    productId: id,
    code: id.toUpperCase(),
    name: `Product ${id}`,
    slug: `product-${id}`,
    brand: null,
    category: null,
    matchedVariantId: `${id}-DEFAULT`,
    matchedSku: `${id}-DEFAULT`,
    price: { amount: 1990, currency: 'EUR' },
    priceFrom: { amount: 1990, currency: 'EUR' },
    priceTo: { amount: 1990, currency: 'EUR' },
    imageId: null,
    inStock: true,
    score: 1,
  };
}

function page(hits: SearchHit[]): SearchResultPage {
  return { hits, total: hits.length, page: 1, pageSize: 10, tookMs: 1, answer: null };
}
