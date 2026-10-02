import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  ProposalDecisionsResponse,
  ProposalFilter,
  ProposalsPage,
  StewardAnswer,
  StewardTurnRequest,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what Steward's endpoints are
 * called (phase 12) — one service per bounded context, as the others: Assist's.
 */
@Injectable({ providedIn: 'root' })
export class StewardService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * One turn. The conversation travels with every turn: the server keeps none.
   * Unsubscribing cancels it — the request is aborted and the server stops the
   * model's work with it.
   */
  turn(request: StewardTurnRequest, culture: string): Observable<StewardAnswer> {
    return this.http.post<StewardAnswer>(`${this.baseUrl}/api/steward/turns`, request, {
      params: new HttpParams().set('culture', culture),
    });
  }

  /** A page of the list of work, with every filter's count. */
  proposals(filter: ProposalFilter, culture: string, page = 1, pageSize = 20): Observable<ProposalsPage> {
    return this.http.get<ProposalsPage>(`${this.baseUrl}/api/steward/proposals`, {
      params: new HttpParams()
        .set('filter', filter)
        .set('culture', culture)
        .set('page', page)
        .set('pageSize', pageSize),
    });
  }

  /** The proposals a turn returned, in its order, whatever became of each since. */
  proposalsById(ids: readonly string[], culture: string): Observable<ProposalsPage> {
    let params = new HttpParams().set('culture', culture);
    for (const id of ids) params = params.append('ids', id);
    return this.http.get<ProposalsPage>(`${this.baseUrl}/api/steward/proposals`, { params });
  }

  /** Runs each proposal's command as whoever is signed in; one row per proposal. */
  approve(ids: readonly string[]): Observable<ProposalDecisionsResponse> {
    return this.http.post<ProposalDecisionsResponse>(`${this.baseUrl}/api/steward/proposals/approve`, { ids });
  }

  /** The reason is required: it is what a change to the prompt is judged against. */
  reject(ids: readonly string[], reason: string): Observable<ProposalDecisionsResponse> {
    return this.http.post<ProposalDecisionsResponse>(`${this.baseUrl}/api/steward/proposals/reject`, { ids, reason });
  }
}
