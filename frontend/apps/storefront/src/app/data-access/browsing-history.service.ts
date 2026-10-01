import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { BrowsingHistoryView, BrowsingKind, MergeHistoryRequest } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * A signed-in customer's trail, kept by Accounts (R-8, H-3b; docs/privacy.md).
 *
 * Every call answers the whole trail as it stands after the change, so the
 * caller replaces what it shows rather than patching it — the server's caps and
 * its 90 days are then the only ones that count. The customer is the caller: no
 * route names one.
 */
@Injectable({ providedIn: 'root' })
export class BrowsingHistoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/api/accounts/me/history`;
  }

  get(): Observable<BrowsingHistoryView> {
    return this.http.get<BrowsingHistoryView>(this.url);
  }

  recordView(code: string): Observable<BrowsingHistoryView> {
    return this.http.post<BrowsingHistoryView>(`${this.url}/views`, { code });
  }

  recordSearch(text: string): Observable<BrowsingHistoryView> {
    return this.http.post<BrowsingHistoryView>(`${this.url}/searches`, { text });
  }

  /** The browser's guest trail, sent once at sign-in. */
  merge(trail: MergeHistoryRequest): Observable<BrowsingHistoryView> {
    return this.http.post<BrowsingHistoryView>(`${this.url}/merge`, trail);
  }

  forget(kind: BrowsingKind, key: string): Observable<BrowsingHistoryView> {
    return this.http.delete<BrowsingHistoryView>(`${this.url}/${kind}/${encodeURIComponent(key)}`);
  }

  clear(): Observable<BrowsingHistoryView> {
    return this.http.delete<BrowsingHistoryView>(this.url);
  }

  /** Off clears what was kept in the same change, and nothing is recorded until it is on again. */
  setEnabled(enabled: boolean): Observable<BrowsingHistoryView> {
    return this.http.put<BrowsingHistoryView>(`${this.url}/settings`, { enabled });
  }
}
