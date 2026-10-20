import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { AccountRemovalView, PersonalDataExport, PreferencesView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The shopper's preferences and their own data (P15-6, P15-9). The customer is
 * the caller: no route names one.
 */
@Injectable({ providedIn: 'root' })
export class ShopperAccountService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get me(): string {
    return `${this.baseUrl}/api/accounts/me`;
  }

  preferences(): Observable<PreferencesView> {
    return this.http.get<PreferencesView>(`${this.me}/preferences`);
  }

  chooseCulture(culture: string): Observable<PreferencesView> {
    return this.http.put<PreferencesView>(`${this.me}/preferences/culture`, { culture });
  }

  setMarketing(given: boolean): Observable<PreferencesView> {
    return this.http.put<PreferencesView>(`${this.me}/preferences/marketing`, { given });
  }

  /** Everything the shop holds about the caller, as one download. */
  data(): Observable<PersonalDataExport> {
    return this.http.get<PersonalDataExport>(`${this.me}/data`);
  }

  /** The account removed: what was forgotten, what is kept and for how long. */
  remove(): Observable<AccountRemovalView> {
    return this.http.delete<AccountRemovalView>(this.me);
  }
}
