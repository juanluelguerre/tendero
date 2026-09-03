import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
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

/**
 * The basket, and what it costs.
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
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly culture = inject(CultureStore);

  private readonly _cart = signal<CartView | null>(null);
  private readonly _quote = signal<PriceQuote | null>(null);
  private readonly _busy = signal(false);
  private readonly _failed = signal<string | null>(null);

  readonly cart = this._cart.asReadonly();
  readonly quote = this._quote.asReadonly();
  readonly busy = this._busy.asReadonly();
  readonly failed = this._failed.asReadonly();

  /** What the header badge shows. Zero renders nothing rather than a "0". */
  readonly itemCount = computed(() => this._cart()?.itemCount ?? 0);

  readonly isEmpty = computed(() => (this._cart()?.lines.length ?? 0) === 0);

  /**
   * The shipping the quote was answered with. It starts at zero and becomes the
   * chosen option's amount at checkout, because `FreeShipping` is a promotion
   * effect and cannot be evaluated without it.
   */
  private shipping = 0;

  get token(): string | null {
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      // A private window, or storage the browser refuses. A shop that threw
      // here would be a shop that does not open.
      return null;
    }
  }

  /** Reads whatever is stored. Safe to call on every page. */
  async load(): Promise<void> {
    if (!this.token) return;

    await this.run(() =>
      firstValueFrom(
        this.http.get<CartView>(`${this.baseUrl}/api/cart`, {
          headers: this.headers(),
          params: this.params(),
          observe: 'response',
        }),
      ),
    );
  }

  async add(sku: string, quantity = 1): Promise<void> {
    await this.run(() =>
      firstValueFrom(
        this.http.post<CartView>(
          `${this.baseUrl}/api/cart/lines`,
          { sku, quantity },
          { headers: this.headers(), params: this.params(), observe: 'response' },
        ),
      ),
    );
  }

  /** Zero removes the line — the same call, because that is what "−" at one
   * unit means. */
  async setQuantity(sku: string, quantity: number): Promise<void> {
    await this.run(() =>
      firstValueFrom(
        this.http.put<CartView>(
          `${this.baseUrl}/api/cart/lines/${encodeURIComponent(sku)}`,
          { quantity },
          { headers: this.headers(), params: this.params(), observe: 'response' },
        ),
      ),
    );
  }

  remove(sku: string): Promise<void> {
    return this.setQuantity(sku, 0);
  }

  /** The delivery options for an address, priced by the same call checkout uses. */
  shippingOptions(address: unknown): Promise<ShippingOptionsResponse> {
    return firstValueFrom(
      this.http.post<ShippingOptionsResponse>(
        `${this.baseUrl}/api/checkout/shipping-options`,
        address,
        { headers: this.headers(), params: this.params() },
      ),
    );
  }

  /**
   * Re-quotes with a shipping amount. Checkout calls it when the option
   * changes, because the total the shopper agrees to includes delivery.
   */
  async quoteWithShipping(amount: number): Promise<PriceQuote | null> {
    this.shipping = amount;
    await this.refreshQuote();
    return this._quote();
  }

  /**
   * The cart is gone: it became an order. Clearing the token is what stops the
   * next page load asking for a basket that was checked out — the API answers
   * an empty one, but the round trip is pure noise.
   */
  clear(): void {
    try {
      localStorage.removeItem(TOKEN_KEY);
    } catch {
      // See `token`.
    }

    this._cart.set(null);
    this._quote.set(null);
    this.shipping = 0;
  }

  private async run(call: () => Promise<{ body: CartView | null; headers: HttpHeaders }>): Promise<void> {
    this._busy.set(true);
    this._failed.set(null);

    try {
      const response = await call();

      // The token comes back on EVERY answer and not only on the first, so the
      // client stores what the last response said rather than having to know
      // when a cart was created.
      const token = response.headers.get(TOKEN_HEADER);
      if (token) this.store(token);

      this._cart.set(response.body);
      await this.refreshQuote();
    } catch (error) {
      this._failed.set(this.describe(error));
    } finally {
      this._busy.set(false);
    }
  }

  /**
   * The live quote. An empty cart is not quoted at all: the endpoint refuses a
   * quote with no lines, and asking anyway would paint an error over a basket
   * somebody just finished emptying.
   */
  private async refreshQuote(): Promise<void> {
    const cart = this._cart();

    if (!cart || cart.lines.length === 0) {
      this._quote.set(null);
      return;
    }

    try {
      this._quote.set(
        await firstValueFrom(
          this.http.post<PriceQuote>(
            `${this.baseUrl}/api/pricing/quote`,
            {
              lines: cart.lines.map((line) => ({ sku: line.sku, quantity: line.quantity })),
              shipping: this.shipping,
            },
            { params: this.params() },
          ),
        ),
      );
    } catch {
      // A basket the shop cannot price is still a basket. The screen says the
      // total is unavailable and keeps the lines, which is more useful than an
      // empty page.
      this._quote.set(null);
    }
  }

  private headers(): HttpHeaders {
    const token = this.token;
    return token ? new HttpHeaders({ [TOKEN_HEADER]: token }) : new HttpHeaders();
  }

  /** The explicit parameter wins over the header (ADR 0013). */
  private params(): HttpParams {
    return new HttpParams().set('culture', this.culture.active());
  }

  private store(token: string): void {
    try {
      localStorage.setItem(TOKEN_KEY, token);
    } catch {
      // See `token`. The cart still works for this page; it just will not
      // survive a reload.
    }
  }

  private describe(error: unknown): string {
    const body = (error as { error?: unknown })?.error;
    return typeof body === 'string' && body.length > 0 ? body : 'cart.failed';
  }
}
