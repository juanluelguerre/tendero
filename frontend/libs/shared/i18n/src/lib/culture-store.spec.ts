import { provideHttpClient } from '@angular/common/http';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { CultureStore } from './culture-store';
import { provideTenderoI18n } from './provide-tendero-i18n';

/**
 * The resolution chain is ADR 0013's, on the client side: an explicit choice
 * beats the browser's languages beats `es`. It has to be the same shape the
 * server uses, or the labels and the products end up disagreeing on the same
 * page.
 */
describe('CultureStore', () => {
  function store(languages: readonly string[], stored?: string): CultureStore {
    localStorage.clear();
    if (stored) localStorage.setItem('tendero.culture', stored);

    // navigator.languages is read-only, so it is redefined per test rather than
    // assigned — the alternative is a wrapper nobody else would ever need.
    Object.defineProperty(navigator, 'languages', { value: languages, configurable: true });

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideTenderoI18n()] });

    return TestBed.inject(CultureStore);
  }

  afterEach(() => localStorage.clear());

  it('takes the browser language when there is no stored choice', () => {
    expect(store(['en-GB', 'en']).active()).toBe('en');
  });

  it('normalises a region to its primary subtag, as the server does', () => {
    expect(store(['es-419']).active()).toBe('es');
  });

  it('walks the preference list rather than reading only the first', () => {
    // French first, English second: English, not the Spanish default. Only the
    // plural navigator.languages can express that.
    expect(store(['fr-FR', 'en-US']).active()).toBe('en');
  });

  it('falls back to Spanish when the browser speaks nothing the shop does', () => {
    expect(store(['de-DE', 'ja']).active()).toBe('es');
  });

  it('lets a stored choice beat the browser, which is the whole point of choosing', () => {
    expect(store(['en-GB'], 'es').active()).toBe('es');
  });

  it('ignores a stored value that is not a culture the shop serves', () => {
    expect(store(['en-GB'], 'kl').active()).toBe('en');
  });

  it('tells Transloco and the document, not just itself', () => {
    const culture = store(['es-ES']);
    culture.use('en');

    // The effect that pushes the culture outwards runs on the next tick, not
    // on assignment. Forcing it here is the test admitting what the browser
    // does for free.
    TestBed.inject(ApplicationRef).tick();

    expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('refuses a culture the shop does not serve', () => {
    const culture = store(['es-ES']);
    culture.use('fr' as never);

    expect(culture.active()).toBe('es');
  });
});
