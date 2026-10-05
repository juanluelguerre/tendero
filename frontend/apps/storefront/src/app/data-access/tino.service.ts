import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { TinoAnswer, TinoAvailability, TinoTurnRequest } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the storefront that knows what Tino's endpoints are called
 * (phase 12b) — Assist's, as the backoffice's Steward service is.
 */
@Injectable({ providedIn: 'root' })
export class TinoService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * One turn. The conversation travels with every turn: the server keeps none.
   * Unsubscribing cancels it — the request is aborted and the server stops the
   * model's work with it.
   */
  turn(request: TinoTurnRequest, culture: string): Observable<TinoAnswer> {
    return this.http.post<TinoAnswer>(`${this.baseUrl}/api/assistant/turns`, request, {
      params: new HttpParams().set('culture', culture),
    });
  }

  /** Whether Tino can be asked. Configured on the server, not probed: no model is called. */
  status(): Observable<TinoAvailability> {
    return this.http.get<TinoAvailability>(`${this.baseUrl}/api/assistant/status`);
  }
}
