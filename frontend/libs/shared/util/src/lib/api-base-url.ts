import { InjectionToken } from '@angular/core';

/**
 * The API's base. In development it is empty and the dev server proxies to the
 * URL Aspire injects (see proxy.conf.mjs): that way not one localhost is written
 * in the application's code.
 *
 * It lives in shared/util and not once per app: it was the same file byte for
 * byte in both. Pure wiring, with no surface identity — exactly the criterion by
 * which Transloco's configuration is already shared (ADR 0010).
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '',
});
