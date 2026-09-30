import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { SearchEvaluation, SearchStatus } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the search endpoints are
 * called. Search is a projection rather than a bounded context, and it still gets
 * its own service: the evaluation is a question about the index, not about the
 * catalogue the index was built from.
 */
@Injectable({ providedIn: 'root' })
export class SearchService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Each stage of search, its state and since when, from the checks the API keeps (R-9, D-7). */
  status(): Observable<SearchStatus> {
    return this.http.get<SearchStatus>(`${this.baseUrl}/api/dashboard/search-status`);
  }

  /** Every golden query, asked of lexical and of hybrid. Seconds, not milliseconds. */
  evaluation(): Observable<SearchEvaluation> {
    return this.http.get<SearchEvaluation>(`${this.baseUrl}/api/search/evaluation`);
  }
}
