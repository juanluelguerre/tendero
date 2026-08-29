import { isDevMode, Provider } from '@angular/core';
import { provideTransloco } from '@jsverse/transloco';
import { TranslocoHttpLoader } from './transloco-http-loader';

/** Las dos culturas del proyecto. El backend resuelve igual: pedida -> en -> primera. */
export const SUPPORTED_CULTURES = ['es', 'en'] as const;
export type Culture = (typeof SUPPORTED_CULTURES)[number];
export const DEFAULT_CULTURE: Culture = 'es';

/**
 * Configuracion compartida de Transloco. Lo unico compartido es el CABLEADO;
 * los ficheros de traduccion viven en cada app, porque el texto no se comparte.
 */
export function provideTenderoI18n(): Provider[] {
  return [
    provideTransloco({
      config: {
        availableLangs: [...SUPPORTED_CULTURES],
        defaultLang: DEFAULT_CULTURE,
        fallbackLang: 'en',
        reRenderOnLangChange: true,
        prodMode: !isDevMode(),
      },
      loader: TranslocoHttpLoader,
    }),
  ];
}
