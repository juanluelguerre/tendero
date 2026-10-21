import { HttpClient } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { ShippingCountries } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL, formatCountry } from '@tendero/shared-util';
import { ISO_COUNTRIES } from './countries';

/** A country as a form's option: its code is the value, its name what is read. */
export interface CountryOption {
  code: string;
  name: string;
}

/**
 * The countries an address can be in, for the account's book and checkout alike.
 *
 * **Which ones is the server's**: the terms checkout quotes with, so a form
 * never offers a country the order would be refused for at the end. The flat
 * rate reaches everywhere and names no list, and then every country is offered.
 * A failed read offers every country too — the server still refuses one it does
 * not reach, so the worst case is the free text there was before, not a lie.
 *
 * Fetched once per application, as the return window is: the answer cannot
 * change while the tab is open.
 */
export const ShippingCountriesStore = signalStore(
  { providedIn: 'root' },
  withProps(() => {
    const http = inject(HttpClient);
    const baseUrl = inject(API_BASE_URL);
    return {
      _culture: inject(CultureStore),
      _reach: extendResource(
        rxResource({ stream: () => http.get<ShippingCountries>(`${baseUrl}/api/shipping/countries`) }),
      ),
    };
  }),
  withComputed(({ _reach, _culture }) => {
    const codes = computed<readonly string[]>(
      () => (_reach.hasValue() ? _reach.value().countries : null) ?? ISO_COUNTRIES,
    );

    return {
      /** By name, in the order of the names in the reader's language: nobody looks for Germany under D. */
      options: computed<CountryOption[]>(() => {
        const culture = _culture.active();
        const order = new Intl.Collator(culture);
        return codes()
          .map((code) => ({ code, name: formatCountry(code, culture) }))
          .sort((one, other) => order.compare(one.name, other.name));
      }),
      /** Whether a code is offered: a kept address in a country no longer reached still shows as itself. */
      offers: computed(() => {
        const offered = new Set(codes());
        return (code: string) => offered.has(code);
      }),
    };
  }),
);
