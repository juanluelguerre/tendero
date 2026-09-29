import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { RankedProducts } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * What sells and what rises, from Ordering's own figures (R-8, H-4): a rank and
 * a product id, never a count — the shop says *Lo más vendido*, not how many.
 * The cards are Search's to draw (`ProductSearchService.cards`).
 */
@Injectable({ providedIn: 'root' })
export class WhatSellsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  bestSellers(limit: number): Observable<RankedProducts> {
    return this.ranked('best-sellers', limit);
  }

  /** The biggest risers of the week against the week before; empty when there are too few orders to say. */
  trending(limit: number): Observable<RankedProducts> {
    return this.ranked('trending', limit);
  }

  private ranked(figure: 'best-sellers' | 'trending', limit: number): Observable<RankedProducts> {
    return this.http.get<RankedProducts>(`${this.baseUrl}/api/ordering/${figure}`, {
      params: new HttpParams().set('limit', limit),
    });
  }
}
