import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ClaimForReview, ReviewClaimResponse } from '@tendero/shared-api';
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

  /** The review queue, already ordered by the server: the weakest support first. */
  claims(limit = 100): Observable<ClaimForReview[]> {
    return this.http.get<ClaimForReview[]>(`${this.baseUrl}/api/knowledge/claims`, {
      params: new HttpParams().set('limit', limit),
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
