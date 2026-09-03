import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { CountedStock, StockList } from '@tendero/shared-api';
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

  /** Every shelf, plus the last few reservations — one screen, one question. */
  stock(culture: string): Observable<StockList> {
    return this.http.get<StockList>(`${this.baseUrl}/api/inventory/stock`, {
      params: { culture },
    });
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
