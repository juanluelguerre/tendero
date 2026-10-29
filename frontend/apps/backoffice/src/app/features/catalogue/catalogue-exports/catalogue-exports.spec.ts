import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { CatalogueExports } from './catalogue-exports';

/**
 * What leaves the catalogue (phase 16, E-2): the product feed's two addresses, one
 * per language, for the shopkeeper to give Merchant Center — opened, or copied.
 */
describe('the catalogue exports', () => {
  beforeEach(() => TestBed.resetTestingModule());

  async function open(apiBase: string) {
    TestBed.configureTestingModule({
      imports: [CatalogueExports],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: apiBase },
      ],
    });
    const fixture = TestBed.createComponent(CatalogueExports);
    fixture.detectChanges();

    // `*transloco` renders nothing until its language arrives: answered empty, since the addresses are asserted and not the words.
    for (const translations of TestBed.inject(HttpTestingController).match((req) => req.url.includes('/i18n/')))
      translations.flush({});
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  /** The element, or a failure that names what was looked for. */
  function find<T extends Element>(root: HTMLElement, selector: string): T {
    const found = root.querySelector<T>(selector);
    if (!found) throw new Error(`Nothing matches ${selector}.`);
    return found;
  }

  function unfold(element: HTMLElement) {
    find<HTMLButtonElement>(element, 'button[aria-expanded]').click();
  }

  it('offers the feed in each language the shop speaks, on the API', async () => {
    const fixture = await open('https://api.example');
    unfold(fixture.nativeElement);
    fixture.detectChanges();

    const links = [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('a[data-feed]')];
    expect(links.map((link) => link.getAttribute('href'))).toEqual([
      'https://api.example/feeds/google/es.tsv',
      'https://api.example/feeds/google/en.tsv',
    ]);
  });

  it('builds the address on the page itself when the API is reached through it', async () => {
    const fixture = await open('');
    unfold(fixture.nativeElement);
    fixture.detectChanges();

    const link = find<HTMLAnchorElement>(fixture.nativeElement, 'a[data-feed]');
    // Written whole, not left for the browser to resolve: it is an address to give Merchant Center.
    expect(link.getAttribute('href')).toBe(`${window.location.origin}/feeds/google/es.tsv`);
  });

  it('takes the catalogue to Shopify when asked', async () => {
    const fixture = await open('https://api.example');
    unfold(fixture.nativeElement);
    fixture.detectChanges();

    find<HTMLButtonElement>(fixture.nativeElement, 'button[data-download="shopify-csv"]').click();

    const request = TestBed.inject(HttpTestingController).expectOne(
      (req) => req.url === 'https://api.example/api/exports/catalogue',
    );
    expect(request.request.params.get('format')).toBe('shopify-csv');
  });

  it('stays folded until it is asked for', async () => {
    const fixture = await open('https://api.example');
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('a[data-feed]')).toBeNull();
    expect(find(element, 'button[aria-expanded]').getAttribute('aria-expanded')).toBe('false');
  });
});
