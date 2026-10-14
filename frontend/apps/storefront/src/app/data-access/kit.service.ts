import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  BuildKitRequest,
  KitSentenceAvailability,
  KitSentenceUnderstood,
  KitTemplate,
  KitView,
} from '@tendero/shared-api';
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

  /** A shopper's sentence read into a kit's form (K-6): which kit, a budget, the requirements. Nothing is built. */
  understand(sentence: string, culture: string): Observable<KitSentenceUnderstood> {
    return this.http.post<KitSentenceUnderstood>(
      `${this.baseUrl}/api/kits/understand`,
      { sentence },
      {
        params: new HttpParams().set('culture', culture),
      },
    );
  }

  /** Whether a sentence can be read at all: configured, not probed, so no model is asked. */
  sentenceStatus(): Observable<KitSentenceAvailability> {
    return this.http.get<KitSentenceAvailability>(`${this.baseUrl}/api/kits/understand/status`);
  }
}
