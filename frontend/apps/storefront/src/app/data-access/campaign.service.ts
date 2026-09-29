import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CampaignList } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The shop's campaigns near now (R-8, ADR 0044): running, and starting within
 * the server's horizon, the leading one first.
 *
 * **The order is the server's rule, never the page's.** Running before upcoming,
 * then priority, then start: the home draws the first whose scope is the shop, a
 * category page the first whose scope is that category, and an agent reads the
 * whole list — three readers, one order.
 *
 * The page, the category banner and the browser agent's `list_campaigns` all call
 * this, which is P6-3's rule: an agent reads what the shopper reads.
 */
@Injectable({ providedIn: 'root' })
export class CampaignService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  list(culture: string): Observable<CampaignList> {
    return this.http.get<CampaignList>(`${this.baseUrl}/api/pricing/campaigns`, {
      params: new HttpParams().set('culture', culture),
    });
  }
}
