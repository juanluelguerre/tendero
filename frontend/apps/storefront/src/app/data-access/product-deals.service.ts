import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { ProductDealsResult } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The promotions one SKU is in, for the product's page (phase 13c's follow-ups):
 * the card has them from the search, the page reads the catalogue and asks Pricing
 * here. Optional by design (invariant 8): a failure costs the line under the price
 * and nothing else.
 */
@Injectable({ providedIn: 'root' })
export class ProductDealsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(sku: string, culture: string): Observable<ProductDealsResult> {
    const params = new HttpParams().set('culture', culture);
    return this.http.get<ProductDealsResult>(`${this.baseUrl}/api/pricing/deals/${encodeURIComponent(sku)}`, {
      params,
    });
  }
}
