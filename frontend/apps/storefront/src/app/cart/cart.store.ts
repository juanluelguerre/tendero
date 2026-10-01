import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { CartView, PriceQuote, ShippingOptionsResponse } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';

/**
 * Where the cart token lives.
 *
 * `localStorage` and not a cookie, deliberately. The token is a bearer
 * credential and it travels in a header, so a cookie would buy automatic
 * sending — which is exactly what makes CSRF possible on a state-changing
 * endpoint. Sending it by hand means only our own code can.
 */
const TOKEN_KEY = 'tendero.cart';

/** The header the API reads it from and echoes it back in. */
const TOKEN_HEADER = 'X-Cart-Token';

interface CartState {
  readonly cart: CartView | null;
  readonly quote: PriceQuote | null;
  readonly busy: boolean;
  readonly failed: string | null;
  /** The token this browser holds, read from storage once and kept in step with it. */
  readonly token: string | null;
  /**
   * The shipping the quote was answered with. It starts at zero and becomes the
   * chosen option's amount at checkout, because `FreeShipping` is a promotion
   * effect and cannot be evaluated without it.
   */
  readonly _shipping: number;
}

/**
 * The basket, and what it costs (ADR 0047: the shop's one cart, read by the
 * header's badge, the product page, the cart, checkout and the browser agent).
 *
 * **Two calls, always, and that is the design.** The cart endpoint returns no
 * money at all — prices are quoted live and frozen at order time (ADR 0016) — so
 * every change is followed by a fresh quote. A store that cached a total would
 * be the second pricing engine the backend refuses to be.
 *
 * The quote is what carries the discounts, including the **suppressed** ones:
 * "not combinable with Summer sale" is a thing the shop should say out loud, and
 * it only exists on the quote.
 */
export const CartStore = signalStore(
  { providedIn: 'root' },
  withState<CartState>(() => ({
    cart: null,
    quote: null,
    busy: false,
    failed: null,
    token: readToken(),
    _shipping: 0,
  })),
  withProps(() => ({
    _http: inject(HttpClient),
    _baseUrl: inject(API_BASE_URL),
    _culture: inject(CultureStore),
  })),
  withComputed(({ cart }) => ({
    /** What the header badge shows. Zero renders nothing rather than a "0". */
    itemCount: computed(() => cart()?.itemCount ?? 0),
    isEmpty: computed(() => (cart()?.lines.length ?? 0) === 0),
  })),
  withMethods((store) => {
    const headers = (): HttpHeaders => {
      const token = store.token();
      return token ? new HttpHeaders({ [TOKEN_HEADER]: token }) : new HttpHeaders();
    };

    /** The explicit parameter wins over the header (ADR 0013). */
    const params = (): HttpParams => new HttpParams().set('culture', store._culture.active());

    const keep = (token: string): void => {
      patchState(store, { token });
      try {
        localStorage.setItem(TOKEN_KEY, token);
      } catch {
        // See `readToken`. The cart still works for this page; it just will not
        // survive a reload.
      }
    };

    /**
     * The live quote. An empty cart is not quoted at all: the endpoint refuses a
     * quote with no lines, and asking anyway would paint an error over a basket
     * somebody just finished emptying.
     */
    const refreshQuote = async (): Promise<void> => {
      const cart = store.cart();
      if (!cart || cart.lines.length === 0) {
        patchState(store, { quote: null });
        return;
      }

      try {
        const quote = await firstValueFrom(
          store._http.post<PriceQuote>(
            `${store._baseUrl}/api/pricing/quote`,
            {
              lines: cart.lines.map((line) => ({ sku: line.sku, quantity: line.quantity })),
              shipping: store._shipping(),
            },
            { params: params() },
          ),
        );
        patchState(store, { quote });
      } catch {
        // A basket the shop cannot price is still a basket. The screen says the
        // total is unavailable and keeps the lines, which is more useful than an
        // empty page.
        patchState(store, { quote: null });
      }
    };

    const run = async (call: () => Promise<{ body: CartView | null; headers: HttpHeaders }>): Promise<void> => {
      patchState(store, { busy: true, failed: null });
      try {
        const response = await call();

        // The token comes back on EVERY answer and not only on the first, so the
        // client stores what the last response said rather than having to know
        // when a cart was created.
        const token = response.headers.get(TOKEN_HEADER);
        if (token) keep(token);

        patchState(store, { cart: response.body });
        await refreshQuote();
      } catch (error) {
        patchState(store, { failed: describe(error) });
      } finally {
        patchState(store, { busy: false });
      }
    };

    /** Reads whatever is stored. Safe to call on every page. */
    const load = async (): Promise<void> => {
      if (!store.token()) return;
      await run(() =>
        firstValueFrom(
          store._http.get<CartView>(`${store._baseUrl}/api/cart`, {
            headers: headers(),
            params: params(),
            observe: 'response',
          }),
        ),
      );
    };

    /** Zero removes the line — the same call, because that is what "−" at one unit means. */
    const setQuantity = (sku: string, quantity: number): Promise<void> =>
      run(() =>
        firstValueFrom(
          store._http.put<CartView>(
            `${store._baseUrl}/api/cart/lines/${encodeURIComponent(sku)}`,
            { quantity },
            { headers: headers(), params: params(), observe: 'response' },
          ),
        ),
      );

    return {
      load,
      setQuantity,
      /**
       * Takes over a cart the shop handed to this browser — an agent's checkout a
       * person finishes here (P11-1). The token replaces whatever basket was
       * stored: the link is for this purchase.
       */
      async adopt(token: string): Promise<void> {
        keep(token);
        await load();
      },
      add(sku: string, quantity = 1): Promise<void> {
        return run(() =>
          firstValueFrom(
            store._http.post<CartView>(
              `${store._baseUrl}/api/cart/lines`,
              { sku, quantity },
              { headers: headers(), params: params(), observe: 'response' },
            ),
          ),
        );
      },
      remove(sku: string): Promise<void> {
        return setQuantity(sku, 0);
      },
      /** The delivery options for an address, priced by the same call checkout uses. */
      shippingOptions(address: unknown): Promise<ShippingOptionsResponse> {
        return firstValueFrom(
          store._http.post<ShippingOptionsResponse>(`${store._baseUrl}/api/checkout/shipping-options`, address, {
            headers: headers(),
            params: params(),
          }),
        );
      },
      /**
       * Re-quotes with a shipping amount. Checkout calls it when the option
       * changes, because the total the shopper agrees to includes delivery.
       */
      async quoteWithShipping(amount: number): Promise<PriceQuote | null> {
        patchState(store, { _shipping: amount });
        await refreshQuote();
        return store.quote();
      },
      /**
       * The cart is gone: it became an order. Clearing the token is what stops the
       * next page load asking for a basket that was checked out — the API answers
       * an empty one, but the round trip is pure noise.
       */
      clear(): void {
        try {
          localStorage.removeItem(TOKEN_KEY);
        } catch {
          // See `readToken`.
        }
        patchState(store, { cart: null, quote: null, token: null, _shipping: 0 });
      },
    };
  }),
);

function readToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    // A private window, or storage the browser refuses. A shop that threw here
    // would be a shop that does not open.
    return null;
  }
}

function describe(error: unknown): string {
  const body = (error as { error?: unknown })?.error;
  return typeof body === 'string' && body.length > 0 ? body : 'cart.failed';
}
