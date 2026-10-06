import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProductReviewStore } from './product-review.store';

const Products = 'http://api/api/catalog/products';

/** A product as publishing it would publish it (ADR 0047): its own read, by the code it is given. */
describe('the product review store', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        ProductReviewStore,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  const reviews = () =>
    http.match((req) => req.url.endsWith('/review')).map((req) => req.request.url.slice(Products.length));

  it('asks nothing until it is given a product', () => {
    TestBed.inject(ProductReviewStore);
    TestBed.tick();

    expect(reviews()).toEqual([]);
  });

  /** The path with no model: what a shopkeeper types is written by the same command an approval runs, then the product is read again. */
  it('writes the alt text a person typed for a picture, in both languages, and reads the product again', async () => {
    const store = TestBed.inject(ProductReviewStore);
    TestBed.runInInjectionContext(() => store.follow(signal('TAZA')));
    TestBed.tick();
    http.expectOne((req) => req.url.endsWith('/TAZA/review')).flush({ productId: 'p1', code: 'TAZA', images: [] });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    store.describe('p1', 'cover', { es: 'Una taza blanca.', en: 'A white mug.' });
    const written = http.expectOne(`${Products}/p1/images/cover/alt`);
    expect([written.request.method, written.request.body, store.saving()]).toEqual([
      'PUT',
      { es: 'Una taza blanca.', en: 'A white mug.' },
      'cover',
    ]);
    written.flush({ productId: 'p1', imageId: 'cover', outcome: 'Described' });
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();

    expect([store.saving(), store.notSaved(), reviews()]).toEqual([null, null, ['/TAZA/review']]);
  });

  /** Refused — a blank language, a sentence too long — the words stay in their boxes and the picture is named as not saved. */
  it('says which picture was not saved when the words are refused, and reads nothing again', () => {
    const store = TestBed.inject(ProductReviewStore);

    store.describe('p1', 'cover', { es: '', en: 'A white mug.' });
    http.expectOne(`${Products}/p1/images/cover/alt`).flush('bad', { status: 400, statusText: 'Bad Request' });

    expect([store.saving(), store.notSaved(), reviews()]).toEqual([null, 'cover', []]);
  });

  it('follows the product it is given', () => {
    const store = TestBed.inject(ProductReviewStore);
    const code = signal('MOCHILA-30L');
    TestBed.runInInjectionContext(() => store.follow(code));
    TestBed.tick();
    const first = reviews();
    code.set('BOLSA LONA');
    TestBed.tick();

    expect([first, reviews()]).toEqual([['/MOCHILA-30L/review'], ['/BOLSA%20LONA/review']]);
  });
});
