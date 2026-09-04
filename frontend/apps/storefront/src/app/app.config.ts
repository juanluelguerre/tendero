import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  inject,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideOAuthClient } from 'angular-oauth2-oidc';
import { AuthStore, tenderoAuthInterceptor } from '@tendero/shared-auth';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes),
    // The OIDC client, and the one call that finishes a redirect.
    //
    // `bootstrap` runs BEFORE the first route resolves, which is what it has to
    // do: the return leg from the issuer arrives as `?code=...` on the
    // application's own origin, and a route that rendered first would show a
    // signed-out page for an instant and then swap. It never throws — an API
    // that is not answering yet means no sign-in, not no application.
    provideOAuthClient(),
    provideAppInitializer(() => inject(AuthStore).bootstrap()),

    // The shop works signed out — that is the whole design, and the cart's
    // 256-bit token is what makes it possible. The interceptor is here for the
    // half that needs a name: an account page and its order history.
    provideHttpClient(withFetch(), withInterceptors([tenderoAuthInterceptor])),
    provideTenderoI18n(),
  ],
};
