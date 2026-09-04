import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CategoryList, CategoryView, OfferList, OfferView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

export type Category = CategoryView;
export type Offer = OfferView;

/**
 * What the front of the shop is made of, beyond the products themselves.
 *
 * The taxonomy and the offers come from two different contexts — `Catalog` owns
 * one and `Pricing` the other — and that separation is real on the server. It is
 * not information a home page needs, so one service asks for both and the page
 * composes them.
 *
 * The types are the GENERATED ones (ADR 0010): clients stay hand-written because
 * they are identity, the shapes are generated because they are behaviour, and a
 * hand-mirrored DTO is a contract that drifts in silence.
 */
@Injectable({ providedIn: 'root' })
export class ShopFrontService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  categories(culture: string): Observable<CategoryList> {
    return this.http.get<CategoryList>(`${this.baseUrl}/api/catalog/categories`, {
      params: new HttpParams().set('culture', culture),
    });
  }

  /**
   * The offers a shopper can actually take, which is a narrower list than the
   * shopkeeper's. The server decides that, not this: a promotion needing a
   * coupon or a segment never reaches the wire.
   */
  offers(culture: string): Observable<OfferList> {
    return this.http.get<OfferList>(`${this.baseUrl}/api/pricing/offers`, {
      params: new HttpParams().set('culture', culture),
    });
  }
}
