import { inject } from '@angular/core';
import { CanActivateFn, RedirectFunction, Route, Router } from '@angular/router';
import { Culture, CultureStore, SUPPORTED_CULTURES } from '@tendero/shared-i18n';
import { Shell } from './layout/shell';

/**
 * The shop, under whichever language segment led to it.
 *
 * One array, mounted once per culture, so `/es/cart` and `/en/cart` are the same
 * five routes and cannot drift apart. Generating them from
 * `SUPPORTED_CULTURES` rather than writing `:culture` is deliberate: a parameter
 * matches ANY first segment, so `/cart` would be a page in a language called
 * "cart" until a guard talked it out of it. With two cultures the explicit form
 * is shorter, and it is the router itself that rejects a language we do not
 * speak.
 */
const shopRoutes: Route[] = [
  {
    path: '',
    loadComponent: () => import('./features/search/search-page').then((m) => m.SearchPage),
  },
  {
    // The slug is decoration and the CODE is the key (ADR 0026): a renamed
    // product keeps this URL working, and the page quietly replaces the slug
    // with the canonical one when they disagree. Slug first because it puts the
    // words a search engine reads earlier in the path — eBay's shape.
    path: 'p/:slug/:code',
    loadComponent: () => import('./features/product/product-page').then((m) => m.ProductPage),
  },
  {
    path: 'account',
    loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
  },
  {
    path: 'cart',
    loadComponent: () => import('./features/cart/cart-page').then((m) => m.CartPage),
  },
  {
    path: 'checkout',
    loadComponent: () => import('./features/checkout/checkout-page').then((m) => m.CheckoutPage),
  },
  {
    // The confirmation page, and the one a shopper comes back to in order to
    // send something back. The id is the credential until phase 7 puts an
    // account behind it — a GUID v7 nobody can enumerate.
    path: 'orders/:id',
    loadComponent: () => import('./features/order/order-page').then((m) => m.OrderPage),
  },
];

/**
 * The URL is what decides the language, not a signal and not `localStorage`.
 *
 * That is the whole point of `P5-13`: while the choice lived only in a store, two
 * people could not look at the same page, a crawler saw one language for two
 * URLs, and there was nothing for `hreflang` to point AT. The stored preference
 * still exists, and it now answers exactly one question — which language to send
 * somebody to when their URL does not say.
 */
export const useCultureFromUrl: CanActivateFn = (route) => {
  inject(CultureStore).use(route.data['culture'] as Culture);
  return true;
};

/**
 * Anything with no language segment gets one, keeping the rest of the path.
 *
 * So `/cart` becomes `/es/cart` rather than a 404, which matters because every
 * link that existed before this change had no language in it — including the
 * order confirmation links already sitting in people's history.
 */
export const toPreferredCulture: RedirectFunction = (data) => {
  const culture = inject(CultureStore).active();

  return inject(Router).createUrlTree(['/', culture, ...data.url.map((segment) => segment.path)], {
    queryParams: data.queryParams,
    fragment: data.fragment ?? undefined,
  });
};

export const appRoutes: Route[] = [
  ...SUPPORTED_CULTURES.map((culture) => ({
    path: culture,
    component: Shell,
    canActivate: [useCultureFromUrl],
    data: { culture },
    children: shopRoutes,
  })),
  { path: '**', redirectTo: toPreferredCulture },
];
