import { HttpClient, HttpHeaders, type HttpResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CartView, PriceQuote, ShippingOptionsResponse } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import type { Observable } from 'rxjs';

/** The header the API reads the cart token from and echoes it back in. */
export const CartTokenHeader = 'X-Cart-Token';

/**
 * The cart's calls and the price quote that follows each change. They lived in
 * `CartStore` until the review of 2026-10-01, the one store that reached for
 * `HttpClient` itself; every other store asks a service here, and so does this
 * one now. The token is the caller's to hand in, and every answer carries it back.
 */
@Injectable({ providedIn: 'root' })
export class CartService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  read(token: string | null, culture: string): Observable<HttpResponse<CartView>> {
    return this.http.get<CartView>(`${this.baseUrl}/api/cart`, this.options(token, culture));
  }

  add(sku: string, quantity: number, token: string | null, culture: string): Observable<HttpResponse<CartView>> {
    return this.http.post<CartView>(`${this.baseUrl}/api/cart/lines`, { sku, quantity }, this.options(token, culture));
  }

  /** Zero removes the line: the same call, because that is what "−" at one unit means. */
  setQuantity(
    sku: string,
    quantity: number,
    token: string | null,
    culture: string,
  ): Observable<HttpResponse<CartView>> {
    return this.http.put<CartView>(
      `${this.baseUrl}/api/cart/lines/${encodeURIComponent(sku)}`,
      { quantity },
      this.options(token, culture),
    );
  }

  quote(
    lines: readonly { sku: string; quantity: number }[],
    shipping: number,
    culture: string,
  ): Observable<PriceQuote> {
    return this.http.post<PriceQuote>(
      `${this.baseUrl}/api/pricing/quote`,
      { lines, shipping },
      { params: this.params(culture) },
    );
  }

  /** The delivery options for an address, priced by the same call checkout uses. */
  shippingOptions(address: unknown, token: string | null, culture: string): Observable<ShippingOptionsResponse> {
    return this.http.post<ShippingOptionsResponse>(`${this.baseUrl}/api/checkout/shipping-options`, address, {
      headers: this.headers(token),
      params: this.params(culture),
    });
  }

  private options(token: string | null, culture: string) {
    return { headers: this.headers(token), params: this.params(culture), observe: 'response' as const };
  }

  private headers(token: string | null): HttpHeaders {
    return token ? new HttpHeaders({ [CartTokenHeader]: token }) : new HttpHeaders();
  }

  /** The explicit parameter wins over the header (ADR 0013). */
  private params(culture: string): HttpParams {
    return new HttpParams().set('culture', culture);
  }
}
