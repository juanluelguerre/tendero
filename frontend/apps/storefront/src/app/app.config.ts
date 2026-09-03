import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter } from '@angular/router';
import { tenderoAuthInterceptor } from '@tendero/shared-auth';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { appRoutes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes),
    // The shop works signed out — that is the whole design, and the cart's
    // 256-bit token is what makes it possible. The interceptor is here for the
    // half that needs a name: an account page and its order history.
    provideHttpClient(withFetch(), withInterceptors([tenderoAuthInterceptor])),
    provideTenderoI18n(),
  ],
};
