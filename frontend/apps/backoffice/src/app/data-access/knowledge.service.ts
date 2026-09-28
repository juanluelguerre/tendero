import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ClaimsForReviewPage, ClaimSupportFilter, ReviewClaimResponse } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/** A person's decision on a claim (invariant 12): the server owns what follows. */
export type ClaimDecision = 'approve' | 'reject';

/**
 * The only place in the backoffice that knows what the Knowledge endpoints are
 * called — one service per bounded context, as the others.
 */
@Injectable({ providedIn: 'root' })
export class KnowledgeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * A page of the review queue, ordered by the server — the weakest support
   * first — narrowed to the weak ones or not, with every filter's count (R-7.2).
   */
  claims(support: ClaimSupportFilter = 'All', page = 1, pageSize = 20): Observable<ClaimsForReviewPage> {
    return this.http.get<ClaimsForReviewPage>(`${this.baseUrl}/api/knowledge/claims`, {
      params: new HttpParams().set('support', support).set('page', page).set('pageSize', pageSize),
    });
  }

  /**
   * Approves or rejects one claim. The reviewer is whoever the session is: the
   * server takes it from the token, so the audit row and the claim agree.
   */
  decide(claimId: string, decision: ClaimDecision): Observable<ReviewClaimResponse> {
    return this.http.post<ReviewClaimResponse>(`${this.baseUrl}/api/knowledge/claims/${claimId}/${decision}`, {});
  }
}
