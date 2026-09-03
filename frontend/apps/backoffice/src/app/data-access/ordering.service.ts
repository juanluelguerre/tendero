import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { OrderDetail, OrderSummary, ReturnView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/** What a shopkeeper can do to an order. The server owns the transition table;
 * this is only the vocabulary. */
export type OrderMove = 'Ship' | 'Deliver' | 'Cancel';

/** And to a return. Same arrangement. */
export type ReturnDecision = 'Approve' | 'Reject' | 'Receive' | 'Refund';

/**
 * The only place in the backoffice that knows what the ordering endpoints are
 * called. One service per bounded context, for the reason the pricing one
 * already states: a service reaching into two is how a frontend starts
 * pretending they are one.
 */
@Injectable({ providedIn: 'root' })
export class OrderingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  orders(): Observable<{ orders: OrderSummary[] }> {
    return this.http.get<{ orders: OrderSummary[] }>(`${this.baseUrl}/api/orders`);
  }

  order(id: string, culture: string): Observable<OrderDetail> {
    return this.http.get<OrderDetail>(`${this.baseUrl}/api/orders/${id}`, {
      params: new HttpParams().set('culture', culture),
    });
  }

  /**
   * Moves an order along. A 409 means the transition table said no, and the
   * screen shows the server's own sentence — reimplementing the table in
   * TypeScript is how the two start disagreeing.
   */
  move(id: string, move: OrderMove, reason?: string): Observable<OrderSummary> {
    return this.http.post<OrderSummary>(`${this.baseUrl}/api/orders/${id}/move`, { move, reason });
  }

  returns(): Observable<ReturnView[]> {
    return this.http.get<ReturnView[]>(`${this.baseUrl}/api/returns`);
  }

  decide(id: string, decision: ReturnDecision, reason?: string): Observable<ReturnView> {
    return this.http.post<ReturnView>(`${this.baseUrl}/api/returns/${id}/decide`, { decision, reason });
  }
}
