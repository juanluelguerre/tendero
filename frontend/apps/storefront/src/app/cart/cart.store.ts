import type { HttpResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { CartView, Money, PriceQuote, ShippingOptionsResponse } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { refusalSentence } from '@tendero/shared-util';
import { catchError, concatMap, defer, finalize, firstValueFrom, type Observable, of, pipe, tap } from 'rxjs';
import { CartService, CartTokenHeader } from '../data-access/cart.service';

/**
 * Where the cart token lives.
 *
 * `localStorage` and not a cookie, deliberately. The token is a bearer
 * credential and it travels in a header, so a cookie would buy automatic
 * sending — which is exactly what makes CSRF possible on a state-changing
 * endpoint. Sending it by hand means only our own code can.
 */
const TOKEN_KEY = 'tendero.cart';

/** A change in the cart's queue, and how to say its turn is over. */
interface CartJob {
  readonly work: () => Observable<unknown>;
  readonly done: () => void;
}

interface CartState {
  readonly cart: CartView | null;
  readonly quote: PriceQuote | null;
  readonly busy: boolean;
  readonly failed: string | null;
  /** The token this browser holds, read from storage once and kept in step with it. */
  readonly token: string | null;
  /**
   * The shipping the quote was answered with. None at first, and the chosen
   * option's amount at checkout, because `FreeShipping` is a promotion effect
   * and cannot be evaluated without it. Money as the option carries it: the
   * quote takes it back as it was sent, currency and all.
   */
  readonly _shipping: Money | null;
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
    _shipping: null,
  })),
  withProps(() => ({
    _cart: inject(CartService),
    _culture: inject(CultureStore),
  })),
  withComputed(({ cart }) => ({
    /** What the header badge shows. Zero renders nothing rather than a "0". */
    itemCount: computed(() => cart()?.itemCount ?? 0),
    isEmpty: computed(() => (cart()?.lines.length ?? 0) === 0),
  })),
  withMethods((store) => {
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
    const quoted = (): Observable<unknown> => {
      const cart = store.cart();
      if (!cart || cart.lines.length === 0) {
        patchState(store, { quote: null });
        return of(null);
      }

      const lines = cart.lines.map((line) => ({ sku: line.sku, quantity: line.quantity }));
      return store._cart.quote(lines, store._shipping(), store._culture.active()).pipe(
        tap((quote) => patchState(store, { quote })),
        // A basket the shop cannot price is still a basket. The screen says the
        // total is unavailable and keeps the lines, which is more useful than an
        // empty page.
        catchError(() => {
          patchState(store, { quote: null });
          return of(null);
        }),
      );
    };

    /** One change and the quote that follows it, start to finish. */
    const change = (call: () => Observable<HttpResponse<CartView>>): Observable<unknown> => {
      patchState(store, { busy: true, failed: null });
      return defer(call).pipe(
        tap((response) => {
          // The token comes back on EVERY answer and not only on the first, so the
          // client stores what the last response said rather than having to know
          // when a cart was created.
          const token = response.headers.get(CartTokenHeader);
          if (token) keep(token);
          patchState(store, { cart: response.body });
        }),
        concatMap(() => quoted()),
        catchError((error: unknown) => {
          patchState(store, { failed: describe(error) });
          return of(null);
        }),
        finalize(() => patchState(store, { busy: false })),
      );
    };

    /**
     * Every change waits for the one before it, answer and quote included:
     * `concatMap`. A click on the product page and the browser agent's tool, or
     * a double click, could otherwise cross on the wire, and a late answer would
     * overwrite a newer basket.
     */
    const queue = rxMethod<CartJob>(pipe(concatMap((job) => job.work().pipe(finalize(() => job.done())))));

    /** Puts a piece of work in the queue; the promise settles when its turn is over. */
    const enqueue = (work: () => Observable<unknown>): Promise<void> =>
      new Promise((done) => queue({ work, done: () => done() }));

    const token = () => store.token();
    const culture = () => store._culture.active();

    /** Reads whatever is stored. Safe to call on every page. */
    const load = (): Promise<void> =>
      store.token() ? enqueue(() => change(() => store._cart.read(token(), culture()))) : Promise.resolve();

    const setQuantity = (sku: string, quantity: number): Promise<void> =>
      enqueue(() => change(() => store._cart.setQuantity(sku, quantity, token(), culture())));

    return {
      load,
      setQuantity,
      /**
       * Takes over a cart the shop handed to this browser — an agent's checkout a
       * person finishes here (P11-1). The token replaces whatever basket was
       * stored: the link is for this purchase.
       */
      async adopt(handedOver: string): Promise<void> {
        keep(handedOver);
        await load();
      },
      add(sku: string, quantity = 1): Promise<void> {
        return enqueue(() => change(() => store._cart.add(sku, quantity, token(), culture())));
      },
      remove(sku: string): Promise<void> {
        return setQuantity(sku, 0);
      },
      /** The delivery options for an address, priced by the same call checkout uses. */
      shippingOptions(address: unknown): Promise<ShippingOptionsResponse> {
        return firstValueFrom(store._cart.shippingOptions(address, token(), culture()));
      },
      /**
       * Re-quotes with a shipping amount. Checkout calls it when the option
       * changes, because the total the shopper agrees to includes delivery. In the
       * queue too: a quote must not overtake the change it prices.
       */
      async quoteWithShipping(amount: Money): Promise<PriceQuote | null> {
        await enqueue(() => {
          patchState(store, { _shipping: amount });
          return quoted();
        });
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
        patchState(store, { cart: null, quote: null, token: null, _shipping: null });
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

/** The shop's own sentence for the refusal, in the reader's language; the screen's own when there is none. */
function describe(error: unknown): string {
  return refusalSentence((error as { error?: unknown })?.error) || 'cart.failed';
}
