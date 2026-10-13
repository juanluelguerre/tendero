import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CultureStore } from './culture-store';

/**
 * Asks our API in the language the application is in.
 *
 * The server picks a culture as `?culture=` → `Accept-Language` → `es`
 * (ADR 0013), and a browser fills `Accept-Language` with its own languages. So
 * a call that named no `?culture=` — most writes, and every refusal they can
 * get (ADR 0059) — was answered in the browser's language while the screen was
 * in the application's: a Spanish backoffice in an English browser said *That
 * agent is already registered*. This makes the header say what the person
 * chose; an explicit `?culture=` still wins, as it does for an agent.
 *
 * Our API and nothing else, as the token is: the issuer's pages are the
 * issuer's to localise.
 */
export const tenderoCultureInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api') || request.headers.has('Accept-Language')) return next(request);

  return next(request.clone({ setHeaders: { 'Accept-Language': inject(CultureStore).active() } }));
};
