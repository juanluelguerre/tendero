import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { PromotionList } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the pricing endpoints are
 * called. Separate from CatalogService rather than folded into it: they are two
 * bounded contexts, and one service reaching into both is how a frontend starts
 * pretending they are one.
 */
@Injectable({ providedIn: 'root' })
export class PricingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Every promotion, running or not, with its combination policy. */
  promotions(): Observable<PromotionList> {
    return this.http.get<PromotionList>(`${this.baseUrl}/api/pricing/promotions`);
  }
}
