import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  OrderDetail,
  OrderView,
  PlaceOrderRequest,
  RequestReturnRequest,
  ReturnView,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom } from 'rxjs';

/** The header the cart token travels in, the same one `CartStore` reads it back from. */
const TOKEN_HEADER = 'X-Cart-Token';

/**
 * Placing an order, reading it back, and sending part of it back.
 *
 * The order page and the checkout page each used to talk to the API on their
 * own, which put `/api/checkout` and `/api/orders/{id}` in two screens and
 * nowhere a browser agent could reach them — the WebMCP tools call the same
 * services the interface calls (P6-3), and a URL that lives only in a page is a
 * URL a tool cannot share. An eslint boundary keeps the screens out of
 * `HttpClient` now, so the next endpoint lands here rather than in a template's
 * component.
 */
@Injectable({ providedIn: 'root' })
export class OrderService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Checkout. The cart token is the credential and travels in its header; the
   * culture goes as the explicit parameter, which wins over `Accept-Language`
   * (ADR 0013) so the confirmation comes back in the language on the screen.
   */
  place(request: PlaceOrderRequest, cartToken: string | null, culture: string): Promise<OrderView> {
    return firstValueFrom(
      this.http.post<OrderView>(`${this.baseUrl}/api/checkout`, request, {
        headers: cartToken ? { [TOKEN_HEADER]: cartToken } : {},
        params: this.params(culture),
      }),
    );
  }

  /** The order and the returns opened against it — one page, one call. */
  get(orderId: string, culture: string): Promise<OrderDetail> {
    return firstValueFrom(
      this.http.get<OrderDetail>(`${this.baseUrl}/api/orders/${encodeURIComponent(orderId)}`, {
        params: this.params(culture),
      }),
    );
  }

  requestReturn(orderId: string, request: RequestReturnRequest, culture: string): Promise<ReturnView> {
    return firstValueFrom(
      this.http.post<ReturnView>(
        `${this.baseUrl}/api/orders/${encodeURIComponent(orderId)}/returns`,
        request,
        { params: this.params(culture) },
      ),
    );
  }

  private params(culture: string): HttpParams {
    return new HttpParams().set('culture', culture);
  }
}
