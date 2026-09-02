import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { Culture, DEFAULT_CULTURE, SUPPORTED_CULTURES } from './provide-tendero-i18n';

const STORAGE_KEY = 'tendero.culture';

/**
 * Which language the interface is in, and the one place that decides it.
 *
 * Transloco has been wired into both apps since the beginning and every template
 * reads from it — but nothing ever called `setActiveLang`, so both apps were
 * permanently Spanish. That is not a cosmetic gap: the active culture is also
 * what travels to the API as `?culture=`, so the whole payoff of phase 2 (the
 * English index going from 0.720 to 0.937 once attribute values were localized)
 * and of phase 3 (a suppressed discount explaining itself in the shopper's
 * language) was unreachable from a browser.
 *
 * **The resolution chain is ADR 0013's, on the client side.** An explicit choice
 * wins, then the browser's own languages, then `es`. That is deliberately the
 * same shape the server uses — explicit `?culture=` beats `Accept-Language`
 * beats the default — because a client that resolved differently from the
 * server would produce a page whose labels and whose products disagreed.
 *
 * It lives in shared because it is BEHAVIOUR (ADR 0010). The CONTROL that
 * switches it is identity and belongs to each app: the storefront's sits in a
 * shop header and the backoffice's in a dense dark bar, and one component that
 * had to be both would be a variant matrix.
 */
@Injectable({ providedIn: 'root' })
export class CultureStore {
  private readonly transloco = inject(TranslocoService);
  private readonly current = signal<Culture>(resolveInitialCulture());

  /** The active culture, as a signal, so anything that formats by culture —
   *  a price, a date, a search request — recomputes when it changes. */
  readonly active = this.current.asReadonly();

  readonly available = SUPPORTED_CULTURES;

  /** Handy for a switcher that renders one button per culture. */
  readonly isActive = computed(() => (culture: Culture) => culture === this.current());

  constructor() {
    effect(() => {
      const culture = this.current();

      this.transloco.setActiveLang(culture);

      // The document's own language, not just Transloco's. A screen reader
      // picks its voice from here, and so does the browser's translate prompt;
      // leaving it as whatever index.html shipped with makes an English page
      // get read out in Spanish.
      document.documentElement.lang = culture;
    });
  }

  use(culture: Culture): void {
    if (!SUPPORTED_CULTURES.includes(culture)) return;

    this.current.set(culture);
    remember(culture);
  }
}

/**
 * Stored choice, then the browser's own languages, then the default.
 *
 * `navigator.languages` and not `navigator.language`: a person whose first
 * preference is French and whose second is English should get English, not
 * Spanish, and only the plural form says that.
 */
function resolveInitialCulture(): Culture {
  const stored = read();
  if (stored) return stored;

  const preferences = navigator.languages?.length ? navigator.languages : [navigator.language];

  for (const preference of preferences) {
    // "en-GB" is English. The server normalises the same way (Culture.Normalize),
    // and the two have to agree or the labels and the products disagree.
    const primary = preference?.split('-')[0]?.toLowerCase();
    const match = SUPPORTED_CULTURES.find((culture) => culture === primary);
    if (match) return match;
  }

  return DEFAULT_CULTURE;
}

/**
 * localStorage can throw (private mode, blocked cookies), and refusing to start
 * because a language preference cannot be remembered would be out of all
 * proportion — the same call the auth store makes about the token.
 */
function read(): Culture | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return SUPPORTED_CULTURES.find((culture) => culture === value) ?? null;
  } catch {
    return null;
  }
}

function remember(culture: Culture): void {
  try {
    localStorage.setItem(STORAGE_KEY, culture);
  } catch {
    // Without persistence the choice lasts as long as the tab. Acceptable.
  }
}
