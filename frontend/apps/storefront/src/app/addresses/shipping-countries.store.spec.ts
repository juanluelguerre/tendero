import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { ISO_COUNTRIES } from './countries';
import { ShippingCountriesStore } from './shipping-countries.store';

/**
 * An address form offers the countries checkout quotes for, by name, in the
 * reader's language — and never a country the shop would refuse at the end.
 */
describe('the countries an address can be in', () => {
  let http: HttpTestingController;
  const active = signal('es');

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://api' },
        { provide: CultureStore, useValue: { active } },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    active.set('es');
  });

  async function answered(countries: string[] | null): Promise<InstanceType<typeof ShippingCountriesStore>> {
    const store = TestBed.inject(ShippingCountriesStore);
    TestBed.tick();
    http.expectOne('http://api/api/shipping/countries').flush({ countries });
    await new Promise((settled) => setTimeout(settled));
    return store;
  }

  it('offers only the countries the shop delivers to, by name and in the order of their names', async () => {
    const store = await answered(['DE', 'ES', 'PT']);

    expect(store.options()).toEqual([
      { code: 'DE', name: 'Alemania' },
      { code: 'ES', name: 'España' },
      { code: 'PT', name: 'Portugal' },
    ]);
  });

  it('names and orders them again when the language changes', async () => {
    const store = await answered(['DE', 'ES', 'PT']);

    active.set('en');

    expect(store.options().map((option) => option.name)).toEqual(['Germany', 'Portugal', 'Spain']);
  });

  /** The flat rate goes everywhere: every country there is, which the server does not list. */
  it('offers every country when the shop delivers to all of them', async () => {
    const store = await answered(null);

    expect(store.options()).toHaveLength(ISO_COUNTRIES.length);
  });

  /** The server still refuses a country it does not reach, so a failed read only widens the list. */
  it('offers every country when the shop could not be asked', () => {
    const store = TestBed.inject(ShippingCountriesStore);
    TestBed.tick();

    http
      .expectOne('http://api/api/shipping/countries')
      .flush('down', { status: 503, statusText: 'Service Unavailable' });

    expect(store.options()).toHaveLength(ISO_COUNTRIES.length);
  });

  /** A kept address in a country no longer reached is still the shopper's: the form shows it, not a blank. */
  it('says whether a code is among those offered', async () => {
    const store = await answered(['ES', 'PT']);

    expect(store.offers()('ES')).toBe(true);
    expect(store.offers()('FR')).toBe(false);
  });
});

describe('the list of every country', () => {
  it('is ISO 3166-1: two capitals each, once each, in order', () => {
    expect(ISO_COUNTRIES).toHaveLength(249);
    expect(ISO_COUNTRIES.every((code) => /^[A-Z]{2}$/.test(code))).toBe(true);
    expect(new Set(ISO_COUNTRIES).size).toBe(ISO_COUNTRIES.length);
    expect([...ISO_COUNTRIES].sort()).toEqual([...ISO_COUNTRIES]);
  });
});
