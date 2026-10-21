import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { AddressBookView, AddressEntryRequest, AddressUse } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * A signed-in customer's address book, kept by Accounts (P15-2).
 *
 * Every call answers the whole book as it stands after the change, so the caller
 * replaces what it shows rather than patching it — the server's rules (one
 * default of each kind, ten at most) are then the only ones that count. The
 * customer is the caller: no route names one.
 */
@Injectable({ providedIn: 'root' })
export class AddressBookService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/api/accounts/me/addresses`;
  }

  get(): Observable<AddressBookView> {
    return this.http.get<AddressBookView>(this.url);
  }

  add(entry: AddressEntryRequest): Observable<AddressBookView> {
    return this.http.post<AddressBookView>(this.url, entry);
  }

  edit(id: string, entry: AddressEntryRequest): Observable<AddressBookView> {
    return this.http.put<AddressBookView>(`${this.url}/${encodeURIComponent(id)}`, entry);
  }

  remove(id: string): Observable<AddressBookView> {
    return this.http.delete<AddressBookView>(`${this.url}/${encodeURIComponent(id)}`);
  }

  /** The entry made the default for delivery or for billing, replacing whichever was. */
  useFor(id: string, use: AddressUse): Observable<AddressBookView> {
    return this.http.put<AddressBookView>(`${this.url}/${encodeURIComponent(id)}/default/${use}`, null);
  }
}
