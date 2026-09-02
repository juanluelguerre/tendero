import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { DEFAULT_CULTURE, provideTenderoI18n, SUPPORTED_CULTURES } from './provide-tendero-i18n';

describe('provideTenderoI18n', () => {
  it('serves the two cultures the project supports, Spanish first', () => {
    // The same two cultures as the search indexes (products_es / products_en)
    // and as the golden sets. If a third appears here with no index behind it,
    // search will come back empty in silence.
    expect(SUPPORTED_CULTURES).toEqual(['es', 'en']);
    expect(DEFAULT_CULTURE).toBe('es');
  });

  it('falls back to English, matching how LocalizedText resolves on the server', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideTenderoI18n()],
    });

    const transloco = TestBed.inject(TranslocoService);

    expect(transloco.getActiveLang()).toBe('es');
    expect(transloco.config.fallbackLang).toBe('en');
    expect(TestBed.inject(HttpClient)).toBeTruthy();
  });
});
