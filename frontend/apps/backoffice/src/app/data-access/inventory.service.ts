import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CountedStock, StockFilter, StockHealth, StockList } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/**
 * The only place in the backoffice that knows what the inventory endpoints are
 * called. One service per bounded context, for the reason PricingService already
 * states: a service reaching into two is how a frontend starts pretending they
 * are one.
 */
@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * The shelves, plus the last few reservations — one screen, one question.
   * Narrowed by a filter, by text in the SKU, and by the SKUs the catalogue
   * matched by name (R-7.1): `skus` present and empty is a search that found no
   * product, which is not the same as no search.
   */
  /**
   * The stock at the shop's pace, for the dashboard (R-9, D-7): coverage over the
   * last four weeks, and the period's sell-through.
   */
  stockHealth(period: 'Day' | 'Week' | 'Month'): Observable<StockHealth> {
    return this.http.get<StockHealth>(`${this.baseUrl}/api/dashboard/stock`, { params: new HttpParams().set('period', period) });
  }

  stock(
    culture: string,
    filter: StockFilter = 'All',
    search?: { text: string; skus: readonly string[] },
    page = 1,
    pageSize = 20,
  ): Observable<StockList> {
    let params = new HttpParams().set('culture', culture).set('filter', filter).set('page', page).set('pageSize', pageSize);
    if (search) {
      params = params.set('q', search.text);
      params = search.skus.length === 0 ? params.set('sku', '') : search.skus.reduce((query, sku) => query.append('sku', sku), params);
    }

    return this.http.get<StockList>(`${this.baseUrl}/api/inventory/stock`, { params });
  }

  /**
   * A stocktake: the shelf holds this many, whatever the system thought.
   *
   * PUT and not PATCH because it is not an increment — it is the count
   * replacing whatever was there, which is exactly what makes it idempotent
   * when a shopkeeper presses twice.
   */
  count(sku: string, warehouseCode: string, onHand: number): Observable<CountedStock> {
    return this.http.put<CountedStock>(
      `${this.baseUrl}/api/inventory/stock/${encodeURIComponent(sku)}/${encodeURIComponent(warehouseCode)}`,
      { onHand },
    );
  }
}
