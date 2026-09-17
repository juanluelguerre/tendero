import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import type { SearchAnswer, SearchHit, SearchResultPage } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { of } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ReturnsPolicy } from '../../data-access/returns-policy.service';
import { SearchPage } from './search-page';

/**
 * What the search page says when a query is not answered by an exact match —
 * the storefront half of layer A (ADR 0032). Three shapes: something close but
 * not exact, nothing sold at all, and a question this shop answers somewhere
 * else (the returns policy). A plain `Exact` answer changes nothing, which is
 * the fourth case and the one most likely to regress silently.
 */
describe('the search page, when the answer is not an exact match', () => {
  let http: HttpTestingController;
  let searchResult: SearchResultPage;
  let returnsWindowDays: number | null;

  beforeEach(() => {
    searchResult = page([], 0, null);
    returnsWindowDays = 14;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideOAuthClient(),
        provideTenderoI18n(),
        provideRouter([{ path: '', component: SearchPage }]),
        { provide: API_BASE_URL, useValue: 'http://api' },
        {
          provide: ProductSearchService,
          useValue: {
            // The effect that reads the URL calls this the moment the page is
            // created, so the fixture is read fresh rather than captured once.
            search: () => of(searchResult),
            browse: () => of(page([], 0, null)),
          } as unknown as ProductSearchService,
        },
        {
          provide: ReturnsPolicy,
          // Read fresh at call time, same reason as `searchResult` above: a
          // test can set it to `null` — unresolved or failed — after this
          // module is configured.
          useValue: { windowDays: () => returnsWindowDays } as unknown as ReturnsPolicy,
        },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  // Anything HomeSections fires during the one-tick 'idle' render (categories,
  // offers) is never asserted on and never answered.
  afterEach(() => http.match(() => true));

  async function open(query: string): Promise<HTMLElement> {
    const harness = await RouterTestingHarness.create(`/?q=${encodeURIComponent(query)}`);

    // `*transloco` renders nothing until its language file arrives.
    for (const translations of http.match((req) => req.url.includes('/i18n/')))
      translations.flush(TRANSLATIONS);

    harness.detectChanges();
    await harness.fixture.whenStable();
    harness.detectChanges();

    const element = harness.routeNativeElement;
    if (!element) throw new Error('The search page did not activate.');
    return element;
  }

  it('shows the notice and the heading when nothing matches exactly', async () => {
    searchResult = page(
      [hit('a'), hit('b')],
      2,
      { match: 'Related', helpTopic: null },
    );

    const element = await open('regalo abuela');

    expect(element.textContent).toContain('Nothing matches “regalo abuela” exactly');
    expect(element.textContent).toContain('The closest things we sell');
    expect(element.querySelectorAll('storefront-product-card')).toHaveLength(2);
  });

  it('shows the returns card above the products for a question about returns', async () => {
    searchResult = page(
      [hit('a')],
      1,
      { match: 'Related', helpTopic: 'returns' },
    );

    const element = await open('how do returns work');

    expect(element.textContent).toContain('Looking for returns?');
    expect(element.textContent).toContain('14 days');

    // DOM order, not just presence: `querySelectorAll` always returns matches
    // in document order regardless of how the selector lists them, so the
    // help card has to come first in this list if it comes first on the page.
    const inOrder = [...element.querySelectorAll('.help-card, storefront-product-card')];
    expect(inOrder).toHaveLength(2);
    expect(inOrder[0].tagName.toLowerCase()).toBe('aside');
  });

  /**
   * `ReturnsPolicy.windowDays` is `null` until its request resolves, and
   * stays `null` if it fails — the storefront's own rule, already proven in
   * `returns-policy.spec.ts`. A page that printed the number anyway would say
   * "You have null days…", which is worse than saying nothing.
   */
  it('says nothing about returns while the window is not known yet', async () => {
    returnsWindowDays = null;
    searchResult = page(
      [hit('a')],
      1,
      { match: 'Related', helpTopic: 'returns' },
    );

    const element = await open('how do returns work');

    expect(element.querySelector('.help-card')).toBeNull();
    expect(element.textContent).not.toContain('Looking for returns?');
    expect(element.textContent).not.toContain('null');
  });

  it('shows nothing extra for an exact answer', async () => {
    searchResult = page(
      [hit('a'), hit('b')],
      2,
      { match: 'Exact', helpTopic: null },
    );

    const element = await open('zapatillas running mujer');

    expect(element.textContent).not.toContain('Looking for returns?');
    expect(element.textContent).not.toContain('exactly');
  });
});

/**
 * Only the keys this page actually reads. Everything else in `search.*`
 * renders as its own key, which is harmless: nothing here asserts on it.
 */
const TRANSLATIONS = {
  search: {
    help: {
      returnsTitle: 'Looking for returns?',
      returnsBody:
        'You have {{days}} days from delivery to send something back. Start a return from your orders.',
      returnsLink: 'Go to my orders',
    },
    related: {
      title: 'Nothing matches “{{query}}” exactly',
      heading: 'The closest things we sell',
    },
  },
};

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
    priceAmount: 19.9,
    priceFrom: 19.9,
    priceTo: 19.9,
    priceCurrency: 'EUR',
    imageId: null,
    inStock: true,
    score: 1,
  };
}

function page(hits: SearchHit[], total: number, answer: SearchAnswer | null): SearchResultPage {
  return { hits, total, page: 1, pageSize: 20, tookMs: 4, answer };
}
