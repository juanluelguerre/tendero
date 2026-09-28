import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ProductSort, SearchResultPage } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the storefront that knows what the search endpoint is
 * called. `/api/search`'s contract does not change even when hybrid search
 * arrives underneath: that is ADR 0004's promise.
 */
@Injectable({ providedIn: 'root' })
export class ProductSearchService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  search(text: string, culture: string, pageSize = 20): Observable<SearchResultPage> {
    return this.query({ q: text, culture, pageSize });
  }

  /**
   * Browsing: the same endpoint with no words in it.
   *
   * It is deliberately the SAME service and the same URL. A shop whose home page
   * rows came from somewhere else would have two ways of listing products, and
   * only one of them would be measured by the relevance gate.
   */
  browse(options: {
    culture: string;
    category?: string;
    sort?: ProductSort;
    page?: number;
    pageSize?: number;
  }): Observable<SearchResultPage> {
    return this.query({ culture: options.culture, category: options.category,
                        sort: options.sort, page: options.page,
                        pageSize: options.pageSize ?? 8 });
  }

  /**
   * A listing with its filters (R-7.3, F-3): the same endpoint again, with the
   * parameters `searchParams` in `listing/listing-filters.ts` builds from the
   * URL, arrays repeated. The answer carries the facets.
   */
  list(params: Record<string, string | string[]>): Observable<SearchResultPage> {
    return this.http.get<SearchResultPage>(`${this.baseUrl}/api/search`, {
      params: new HttpParams({ fromObject: params }),
    });
  }

  private query(options: {
    q?: string;
    culture: string;
    category?: string;
    sort?: string;
    page?: number;
    pageSize?: number;
  }): Observable<SearchResultPage> {
    let params = new HttpParams().set('culture', options.culture);

    // Only what was asked for: an empty `q=` and an empty `category=` are both
    // valid to send and both add noise to a URL somebody may share.
    if (options.q) params = params.set('q', options.q);
    if (options.category) params = params.set('category', options.category);
    if (options.sort) params = params.set('sort', options.sort);
    // Page 1 is the default on both sides, so sending it says nothing.
    if (options.page && options.page > 1) params = params.set('page', options.page);
    if (options.pageSize) params = params.set('pageSize', options.pageSize);

    return this.http.get<SearchResultPage>(`${this.baseUrl}/api/search`, { params });
  }
}
