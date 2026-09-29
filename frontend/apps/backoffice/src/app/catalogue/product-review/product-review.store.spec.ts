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
      providers: [provideHttpClient(), provideHttpClientTesting(), provideTenderoI18n(), { provide: API_BASE_URL, useValue: 'http://api' }, ProductReviewStore],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  const reviews = () => http.match((req) => req.url.endsWith('/review')).map((req) => req.request.url.slice(Products.length));

  it('asks nothing until it is given a product', () => {
    TestBed.inject(ProductReviewStore);
    TestBed.tick();

    expect(reviews()).toEqual([]);
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
