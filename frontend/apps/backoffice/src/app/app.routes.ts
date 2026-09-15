import { inject } from '@angular/core';
import { CanActivateFn, Route, Router } from '@angular/router';
import { AuthStore } from '@tendero/shared-auth';
import { backofficeAccess } from './access';
import { Shell } from './layout/shell';

/**
 * Every screen here is a shopkeeper's, and the API already refuses them to
 * anybody else. The guard exists so the interface does not offer what the
 * server is going to deny: without it, the review queue would load empty with a
 * 401 in the console and nothing to explain why.
 *
 * It asks about the ROLE and not only about a session. The first version asked
 * whether anybody was signed in, and a shopper walked in (UI review of
 * 2026-09-11): every tab, an import button the API refuses, and each 403 shown
 * as "could not be loaded" — the exact failure the sentence above describes,
 * one role further along.
 */
const shopkeeper: CanActivateFn = (_route, state) => {
  const router = inject(Router);

  switch (backofficeAccess(inject(AuthStore).identity())) {
    case 'allowed':
      return true;

    case 'not-a-shopkeeper':
      // No `returnTo`: signing in again as the same person would bring them
      // straight back here. The door says who they are and offers to sign out.
      return router.createUrlTree(['/sign-in'], { queryParams: { reason: 'role' } });

    case 'signed-out':
      // Where they were going, so the door can send them back to it. Without
      // this, signing in from the door returns you to the door: the path
      // travels in the OAuth `state`, and the path at that moment is `/sign-in`.
      return router.createUrlTree(['/sign-in'], { queryParams: { returnTo: state.url } });
  }
};

export const appRoutes: Route[] = [
  {
    path: '',
    component: Shell,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'review' },
      {
        path: 'sign-in',
        title: 'signIn.title',
        loadComponent: () =>
          import('./features/sign-in/sign-in-page').then((m) => m.SignInPage),
      },
      {
        path: 'products/:id/variants',
        title: 'variants.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/variants/define-variants-page').then((m) => m.DefineVariantsPage),
      },
      {
        path: 'audit',
        title: 'audit.title',
        canActivate: [shopkeeper],
        loadComponent: () => import('./features/audit/audit-page').then((m) => m.AuditPage),
      },
      {
        path: 'attributes',
        title: 'attributes.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/attributes/attributes-page').then((m) => m.AttributesPage),
      },
      {
        path: 'search',
        title: 'searchEvaluation.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/search-evaluation/search-evaluation-page').then(
            (m) => m.SearchEvaluationPage,
          ),
      },
      {
        path: 'promotions',
        title: 'promotions.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/promotions/promotions-page').then((m) => m.PromotionsPage),
      },
      {
        path: 'stock',
        title: 'stock.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/stock/stock-page').then((m) => m.StockPage),
      },
      {
        path: 'orders',
        title: 'orders.title',
        canActivate: [shopkeeper],
        loadComponent: () => import('./features/orders/orders-page').then((m) => m.OrdersPage),
      },
      {
        path: 'returns',
        title: 'returns.title',
        canActivate: [shopkeeper],
        loadComponent: () => import('./features/returns/returns-page').then((m) => m.ReturnsPage),
      },
      {
        path: 'catalogue',
        title: 'catalogue.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/catalogue/catalogue-page').then((m) => m.CataloguePage),
      },
      {
        path: 'review',
        title: 'reviewQueue.title',
        canActivate: [shopkeeper],
        loadComponent: () =>
          import('./features/review-queue/review-queue-page').then((m) => m.ReviewQueuePage),
      },
    ],
  },
];
