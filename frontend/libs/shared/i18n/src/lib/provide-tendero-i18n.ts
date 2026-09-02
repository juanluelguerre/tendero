import { isDevMode, Provider } from '@angular/core';
import { provideTransloco } from '@jsverse/transloco';
import { TranslocoHttpLoader } from './transloco-http-loader';

/** The project's two cultures. The backend resolves the same way: requested -> en -> first. */
export const SUPPORTED_CULTURES = ['es', 'en'] as const;
export type Culture = (typeof SUPPORTED_CULTURES)[number];
export const DEFAULT_CULTURE: Culture = 'es';

/**
 * Transloco's shared configuration. The only thing shared is the WIRING; the
 * translation files live in each app, because the words are not shared.
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
