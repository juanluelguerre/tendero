import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { KeepCardRequest, WalletView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * A signed-in customer's saved ways to pay, kept by Ordering (P15-4, ADR 0064).
 *
 * Every call answers the whole wallet as it stands after the change. No token
 * crosses this service: a method is what may be shown and an id.
 */
@Injectable({ providedIn: 'root' })
export class WalletService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/api/accounts/me/payment-methods`;
  }

  get(): Observable<WalletView> {
    return this.http.get<WalletView>(this.url);
  }

  /** A card kept from what the provider's own field produced. */
  keep(card: KeepCardRequest): Observable<WalletView> {
    return this.http.post<WalletView>(this.url, card);
  }

  /** Forgotten at the provider first; gone from the wallet either way. */
  remove(id: string): Observable<WalletView> {
    return this.http.delete<WalletView>(`${this.url}/${encodeURIComponent(id)}`);
  }

  useByDefault(id: string): Observable<WalletView> {
    return this.http.put<WalletView>(`${this.url}/${encodeURIComponent(id)}/default`, null);
  }
}
