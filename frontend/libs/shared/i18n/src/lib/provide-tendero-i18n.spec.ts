import { HttpClient } from '@angular/common/http';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { DEFAULT_CULTURE, provideTenderoI18n, SUPPORTED_CULTURES } from './provide-tendero-i18n';

describe('provideTenderoI18n', () => {
  it('serves the two cultures the project supports, Spanish first', () => {
    // Las mismas dos culturas que los indices de busqueda (products_es /
    // products_en) y que los golden sets. Si aqui aparece una tercera sin
    // indice detras, la busqueda devolvera vacio en silencio.
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
