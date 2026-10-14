import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { provideTenderoI18n } from './provide-tendero-i18n';
import { PageTitles, provideTranslatedTitles } from './translated-titles';

/**
 * Every tab of the shop said "Tendero Shop" — the cart, the checkout and a
 * product page alike — so a browser history, a bookmark and a search result
 * all read the same (UI review, 2026-09-11). A page is named by its route in
 * the reader's language, and a page whose name is data names itself.
 */
@Component({ template: '' })
class Blank {}

describe('page titles', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    Object.defineProperty(navigator, 'languages', { value: ['es-ES'], configurable: true });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        provideTranslatedTitles(),
        provideRouter([
          { path: '', component: Blank },
          { path: 'cart', component: Blank, title: 'cart.title' },
        ]),
      ],
    });

    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function arrive(path: string): Promise<void> {
    await TestBed.inject(Router).navigateByUrl(path);

    for (const file of http.match((req) => req.url.includes('/i18n/')))
      file.flush({ brand: { title: 'Tendero' }, cart: { title: 'Tu cesta' } });

    await TestBed.inject(Router).navigateByUrl(path);
  }

  it('names a page by its route, in the reader language, after the brand', async () => {
    await arrive('/cart');

    expect(TestBed.inject(Title).getTitle()).toBe('Tu cesta · Tendero');
  });

  it('is the brand alone on a page with no name of its own', async () => {
    await arrive('/');

    expect(TestBed.inject(Title).getTitle()).toBe('Tendero');
  });

  it('lets a page whose name is data name itself', async () => {
    await arrive('/cart');

    TestBed.inject(PageTitles).name('Zapatillas Pulse Runner');

    expect(TestBed.inject(Title).getTitle()).toBe('Zapatillas Pulse Runner · Tendero');
  });

  /**
   * Found in a browser on 2026-10-13: a category's tab said *Hogar · Tendero*
   * until the first filter, and *Sección · Tendero* ever after. A filter is a
   * navigation — the address gains `?price=-30` — and every navigation put the
   * route's word back, while the page, still showing the same category, had
   * no reason to name itself again. The same page under another query is the
   * same page.
   */
  it('keeps a page its own name while only the query changes', async () => {
    await arrive('/cart');
    TestBed.inject(PageTitles).name('Hogar');

    await TestBed.inject(Router).navigateByUrl('/cart?price=-30');

    expect(TestBed.inject(Title).getTitle()).toBe('Hogar · Tendero');
  });

  it('gives the name up when the page is another one', async () => {
    await arrive('/cart');
    TestBed.inject(PageTitles).name('Hogar');

    await TestBed.inject(Router).navigateByUrl('/');

    expect(TestBed.inject(Title).getTitle()).toBe('Tendero');
  });
});
