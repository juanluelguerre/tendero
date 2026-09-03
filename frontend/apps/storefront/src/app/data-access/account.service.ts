import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CartView, LinkedIdentity, MyOrders } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { firstValueFrom, Observable } from 'rxjs';

/**
 * Signing in, and what it takes.
 *
 * **Two requests, in two contexts, and the split is the architecture showing
 * through.** `Accounts` answers who you are; the basket belongs to `Ordering`.
 * One endpoint doing both would mean one context reaching into the other — and
 * Ordering will need Accounts for order history, so a dependency that way closes
 * a cycle.
 *
 * The order matters: the customer must exist before a cart can be attached to
 * it, which is why `link` is awaited before `claimCart`.
 */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Establishes the customer behind the token.
   *
   * The SUBJECT is never sent: it comes from the validated token, and a body
   * that could name one would let anybody claim anybody's account. The guest id
   * is optional and is the caller's OWN — supplying somebody else's is refused
   * by the server, which merges nothing that is not a guest.
   */
  link(displayName: string | null, culture: string, guest?: string): Promise<LinkedIdentity> {
    return firstValueFrom(
      this.http.post<LinkedIdentity>(`${this.baseUrl}/api/accounts/me`, {
        displayName,
        culture,
        guest: guest ?? null,
      }),
    );
  }

  /**
   * Attaches the basket that was being carried as a guest.
   *
   * The cart token travels in its header, as it always does — the CartStore owns
   * that credential and this only asks the browser to send it, which the
   * interceptor already arranges for our own API.
   */
  claimCart(token: string): Promise<CartView> {
    return firstValueFrom(
      this.http.post<CartView>(`${this.baseUrl}/api/cart/claim`, null, {
        headers: { 'X-Cart-Token': token },
      }),
    );
  }

  orders(): Observable<MyOrders> {
    return this.http.get<MyOrders>(`${this.baseUrl}/api/orders/mine`);
  }
}
