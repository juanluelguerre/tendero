import { inject } from '@angular/core';
import { CanActivateFn, RedirectFunction, Route, Router, UrlMatcher } from '@angular/router';
import { Culture, CultureStore, SUPPORTED_CULTURES } from '@tendero/shared-i18n';
import { Shell } from './shell/shell';

/**
 * The shop, under whichever language segment led to it.
 *
 * One array under one route for every language (`cultureSegment`), so `/es/cart`
 * and `/en/cart` are the same routes and cannot drift apart, and switching
 * language changes a parameter rather than the route: the shell, Tino's
 * conversation and the page are kept (the author, 2026-10-14). It was one route
 * per language until then, and the switch destroyed and remade all three. Not a
 * bare `:culture`, which matches ANY first segment and would make `/cart` a page
 * in a language called "cart": the matcher takes a segment only when it is a
 * language the shop speaks, so the router itself still rejects the rest.
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
    title: 'product.title',
    loadComponent: () => import('./features/product/product-page').then((m) => m.ProductPage),
  },
  {
    // `c` for category, matching `p` for product. The CODE and not the name: a
    // name is not stable and this one exists in two languages, which is the same
    // argument ADR 0026 makes about products — except that a category has no
    // slug to decorate the URL with, so there is nothing to put in front of it.
    path: 'c/:code',
    title: 'category.title',
    loadComponent: () => import('./features/category/category-page').then((m) => m.CategoryPage),
  },
  {
    // The answer to a picture (phase 13a). The picture is in the page's memory and
    // never in the address: there is nothing here to share, and a reload asks again.
    path: 'search/picture',
    title: 'visualSearch.pageTitle',
    loadComponent: () => import('./features/visual-search/visual-search-page').then((m) => m.VisualSearchPage),
  },
  {
    // The products are in the query, not the path: a comparison is a set, its
    // order is the shopper's, and a URL that carries it can be shared (P10-7).
    path: 'compare',
    title: 'compare.title',
    loadComponent: () => import('./features/compare/compare-page').then((m) => m.ComparePage),
  },
  {
    // The shop's kits (phase 13c, K-5): the templates it wrote, and one of
    // them built for a budget. The kit page names itself once it knows which.
    path: 'kits',
    title: 'kits.title',
    loadComponent: () => import('./features/kits/kits-page').then((m) => m.KitsPage),
  },
  {
    path: 'kits/:code',
    title: 'kits.kitTitle',
    loadComponent: () => import('./features/kits/kit-page').then((m) => m.KitPage),
  },
  {
    // An agent's checkout that needs a person (UCP's requires_escalation): the
    // link it gives them lands here, takes over the cart and goes to checkout.
    path: 'checkout/continue/:sessionId',
    title: 'checkout.title',
    loadComponent: () => import('./features/checkout/continue-checkout-page').then((m) => m.ContinueCheckoutPage),
  },
  {
    // *Compra con tu agente* (R-8, H-5): how to point an assistant at the shop.
    path: 'agents',
    title: 'agents.title',
    loadComponent: () => import('./features/agents/agents-page').then((m) => m.AgentsPage),
  },
  {
    path: 'account',
    title: 'account.title',
    loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
  },
  {
    // The address book (P15-2): the list, then one entry added or edited.
    path: 'account/addresses',
    title: 'addresses.title',
    loadComponent: () => import('./features/account/addresses-page').then((m) => m.AddressesPage),
  },
  {
    path: 'account/addresses/new',
    title: 'addresses.addTitle',
    loadComponent: () => import('./features/account/address-form-page').then((m) => m.AddressFormPage),
  },
  {
    // Preferences and the shopper's own data (P15-6, P15-9).
    path: 'account/preferences',
    title: 'preferences.title',
    loadComponent: () => import('./features/account/preferences-page').then((m) => m.PreferencesPage),
  },
  {
    path: 'account/your-data',
    title: 'yourData.title',
    loadComponent: () => import('./features/account/your-data-page').then((m) => m.YourDataPage),
  },
  {
    // Saved ways to pay (P15-4).
    path: 'account/ways-to-pay',
    title: 'wallet.title',
    loadComponent: () => import('./features/account/ways-to-pay-page').then((m) => m.WaysToPayPage),
  },
  {
    path: 'account/addresses/:id',
    title: 'addresses.editTitle',
    loadComponent: () => import('./features/account/address-form-page').then((m) => m.AddressFormPage),
  },
  {
    path: 'cart',
    title: 'cart.title',
    loadComponent: () => import('./features/cart/cart-page').then((m) => m.CartPage),
  },
  {
    path: 'checkout',
    title: 'checkout.title',
    loadComponent: () => import('./features/checkout/checkout-page').then((m) => m.CheckoutPage),
  },
  {
    // The confirmation page, and the one a shopper comes back to in order to
    // send something back. For a guest the id is the credential — a GUID v7
    // nobody can enumerate; a signed-in shopper also reaches it from their
    // account (phase 7).
    path: 'orders/:id',
    title: 'order.title',
    loadComponent: () => import('./features/order/order-page').then((m) => m.OrderPage),
  },
  {
    // Anything else inside a language is a page that does not exist, answered at
    // the address asked for. Without it the path fell through to the redirect
    // below, which added the language again, and again (app.routes.spec.ts).
    path: '**',
    title: 'notFound.title',
    loadComponent: () => import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
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
  inject(CultureStore).use(route.paramMap.get('culture') as Culture);
  return true;
};

/**
 * Anything with no language segment gets one, keeping the rest of the path. Only
 * that: a path that has one and matches nothing is the language's own `**`.
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

/** The first segment, when it is a language the shop speaks, as the parameter `culture`. */
export const cultureSegment: UrlMatcher = (segments) =>
  segments.length > 0 && (SUPPORTED_CULTURES as readonly string[]).includes(segments[0].path)
    ? { consumed: [segments[0]], posParams: { culture: segments[0] } }
    : null;

export const appRoutes: Route[] = [
  {
    matcher: cultureSegment,
    component: Shell,
    // Runs again when only the language changes — the default, written here
    // because it is the point: the switch is a change of parameter.
    runGuardsAndResolvers: 'paramsChange',
    canActivate: [useCultureFromUrl],
    children: shopRoutes,
  },
  { path: '**', redirectTo: toPreferredCulture },
];
