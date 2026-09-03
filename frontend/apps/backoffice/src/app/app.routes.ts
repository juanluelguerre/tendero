import { inject } from '@angular/core';
import { Route, Router } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { Shell } from './layout/shell';

/**
 * Publishing is a shopkeeper's operation, and the API already refuses it without
 * a token. The guard exists so the interface does not offer what the server is
 * going to deny: without it, the review queue would load empty with a 401 in the
 * console and nothing to explain why.
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
        path: 'audit',
        canActivate: [signedIn],
        loadComponent: () => import('./features/audit/audit-page').then((m) => m.AuditPage),
      },
      {
        path: 'attributes',
        canActivate: [signedIn],
        loadComponent: () =>
          import('./features/attributes/attributes-page').then((m) => m.AttributesPage),
      },
      {
        path: 'promotions',
        canActivate: [signedIn],
        loadComponent: () =>
          import('./features/promotions/promotions-page').then((m) => m.PromotionsPage),
      },
      {
        path: 'stock',
        canActivate: [signedIn],
        loadComponent: () =>
          import('./features/stock/stock-page').then((m) => m.StockPage),
      },
      {
        path: 'orders',
        canActivate: [signedIn],
        loadComponent: () => import('./features/orders/orders-page').then((m) => m.OrdersPage),
      },
      {
        path: 'returns',
        canActivate: [signedIn],
        loadComponent: () => import('./features/returns/returns-page').then((m) => m.ReturnsPage),
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
