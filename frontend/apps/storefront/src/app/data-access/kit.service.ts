import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { BuildKitRequest, KitTemplate, KitView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The shop's kits (phase 13c): the templates it wrote, and one of them solved
 * for a budget. The kit carries each product's SKU and no quote — the page
 * asks for that through the call the cart already makes (`CartService.quote`),
 * because the solver's slice does not reach Pricing.
 */
@Injectable({ providedIn: 'root' })
export class KitService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(culture: string): Observable<KitTemplate[]> {
    return this.http.get<KitTemplate[]>(`${this.baseUrl}/api/kits`, {
      params: new HttpParams().set('culture', culture),
    });
  }

  build(template: string, request: BuildKitRequest, culture: string): Observable<KitView> {
    return this.http.post<KitView>(`${this.baseUrl}/api/kits/${encodeURIComponent(template)}/build`, request, {
      params: new HttpParams().set('culture', culture),
    });
  }
}
