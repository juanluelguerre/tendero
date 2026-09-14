import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CataloguePage } from './catalogue-page';

/**
 * The published catalogue, and the way to its variants.
 *
 * Defining variants was only offered from the review queue, which lists
 * drafts. Once the hundred seeded products were published — all of them — the
 * backoffice had no screen listing a single one, so "define variants" (P1-8)
 * could not be reached through the interface at all, and the storefront's
 * picker had never met a product with axes. Found by the UI review of
 * 2026-09-11.
 */
describe('the catalogue page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CataloguePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  function open() {
    const fixture = TestBed.createComponent(CataloguePage);
    fixture.detectChanges();
    return fixture;
  }

  it('asks for what is published, not for what is under review', () => {
    open();

    const request = http.expectOne((req) => req.url === 'http://api/api/catalog/products');

    expect(request.request.params.get('status')).toBe('active');
  });

  it('offers the variants of every published product', async () => {
    const fixture = open();

    http.expectOne((req) => req.url === 'http://api/api/catalog/products').flush({
      items: [product('p-1', 'Zapatillas Pulse Runner'), product('p-2', 'Camiseta técnica')],
      total: 2,
      page: 1,
      pageSize: 20,
    });

    // `*transloco` renders nothing until its language arrives, so the pending
    // translation file is answered too — empty, since what is asserted is the
    // links and not the words.
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});

    fixture.detectChanges();
    await fixture.whenStable();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll('a')]
      .map((link) => link.getAttribute('href'));

    expect(links).toEqual(['/products/p-1/variants', '/products/p-2/variants']);
  });
});

function product(productId: string, name: string) {
  return {
    productId,
    code: 'CODE',
    name,
    slug: 'slug',
    brand: 'Vertex',
    category: null,
    priceAmount: 79.95,
    priceCurrency: 'EUR',
    imageId: null,
    status: 'Active',
    missingCultures: [],
    updatedAt: '2026-09-11T00:00:00Z',
  };
}
