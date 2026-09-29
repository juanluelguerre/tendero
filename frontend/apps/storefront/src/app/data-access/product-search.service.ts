import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ProductCardsResult, ProductSort, SearchResultPage, SimilarProductsResult } from '@tendero/shared-api';
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
    /** What is on offer now: the home's *Ofertas* row (R-8, H-4). */
    onOffer?: boolean;
    /** What a campaign's promotions reach now: the campaign card's products (R-8, H-2). */
    campaign?: string;
  }): Observable<SearchResultPage> {
    return this.query({ ...options, pageSize: options.pageSize ?? 8 });
  }

  /**
   * Cards for products named by id or by code, in the order asked, leaving out
   * any that can no longer be shown (R-8): what sells and what rises arrive as
   * ids, the trail as codes, and both are drawn with the listing's own card.
   */
  cards(culture: string, which: { ids?: readonly string[]; codes?: readonly string[] }): Observable<ProductCardsResult> {
    const params: Record<string, string | string[]> = { culture };
    if (which.ids?.length) params['ids'] = [...which.ids];
    if (which.codes?.length) params['codes'] = [...which.codes];
    return this.http.get<ProductCardsResult>(`${this.baseUrl}/api/search/cards`, {
      params: new HttpParams({ fromObject: params }),
    });
  }

  /**
   * *Puede que te guste* (R-8, H-4b): products like the ones viewed, each with
   * the code it came from, so *Porque viste…* is data and not wording the page
   * makes up.
   */
  similar(culture: string, codes: readonly string[], limit: number): Observable<SimilarProductsResult> {
    return this.http.get<SimilarProductsResult>(`${this.baseUrl}/api/search/similar`, {
      params: new HttpParams({ fromObject: { culture, codes: [...codes], limit: String(limit) } }),
    });
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
    onOffer?: boolean;
    campaign?: string;
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
    if (options.onOffer) params = params.set('onOffer', true);
    if (options.campaign) params = params.set('campaign', options.campaign);

    return this.http.get<SearchResultPage>(`${this.baseUrl}/api/search`, { params });
  }
}
