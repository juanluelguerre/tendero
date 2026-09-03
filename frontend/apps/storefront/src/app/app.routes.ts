import { Route } from '@angular/router';
import { Shell } from './layout/shell';

export const appRoutes: Route[] = [
  {
    path: '',
    component: Shell,
    children: [
      {
        path: '',
        loadComponent: () => import('./features/search/search-page').then((m) => m.SearchPage),
      },
      {
        path: 'cart',
        loadComponent: () => import('./features/cart/cart-page').then((m) => m.CartPage),
      },
      {
        path: 'checkout',
        loadComponent: () =>
          import('./features/checkout/checkout-page').then((m) => m.CheckoutPage),
      },
      {
        // The confirmation page, and the one a shopper comes back to in order
        // to send something back. The id is the credential until phase 7 puts
        // an account behind it — a GUID v7 nobody can enumerate.
        path: 'orders/:id',
        loadComponent: () => import('./features/order/order-page').then((m) => m.OrderPage),
      },
    ],
  },
];
