import { inject } from '@angular/core';
import { Route, Router } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { Shell } from './layout/shell';

/**
 * Publicar es una operacion de tendero, y la API ya la rechaza sin token. El
 * guardia existe para que la interfaz no ofrezca lo que el servidor va a
 * denegar: sin el, la cola de revision cargaria vacia con un 401 en la consola
 * y sin nada que explique por que.
 */
const signedIn = () => {
  const router = inject(Router);
  return inject(AuthStore).isSignedIn() ? true : router.createUrlTree(['/sign-in']);
};

export const appRoutes: Route[] = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'review' },
      {
        path: 'sign-in',
        loadComponent: () =>
          import('./features/sign-in/sign-in-page').then((m) => m.SignInPage),
      },
      {
        path: 'products/:id/variants',
        canActivate: [signedIn],
        loadComponent: () =>
          import('./features/variants/define-variants-page').then((m) => m.DefineVariantsPage),
      },
      {
        path: 'review',
        canActivate: [signedIn],
        loadComponent: () =>
          import('./features/review-queue/review-queue-page').then((m) => m.ReviewQueuePage),
      },
    ],
  },
];
