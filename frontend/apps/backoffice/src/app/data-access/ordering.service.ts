import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type {
  AgentSales,
  CheckoutFunnel,
  ListOrdersResult,
  OperationsSummary,
  OrderDetail,
  OrderSummary,
  ReturnView,
  SalesSummary,
} from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { Observable } from 'rxjs';

/** What a shopkeeper can do to an order. The server owns the transition table;
 * this is only the vocabulary. */
export type OrderMove = 'Ship' | 'Deliver' | 'Cancel';

/** And to a return. Same arrangement. */
export type ReturnDecision = 'Approve' | 'Reject' | 'Receive' | 'Refund';

/** The window a dashboard figure covers, against the one before it (P11-7). */
export type DashboardPeriod = 'day' | 'week' | 'month';

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

  /** Narrowed by status and by the start or end of an id, or any part of a name or city (R-4.2). */
  orders(status: string | null = null, text = ''): Observable<ListOrdersResult> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (text.trim()) params = params.set('q', text.trim());
    return this.http.get<ListOrdersResult>(`${this.baseUrl}/api/orders`, { params });
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

  // ---------- The dashboard's figures (P11-7): one endpoint each ----------

  sales(period: DashboardPeriod): Observable<SalesSummary> {
    return this.figure<SalesSummary>('sales', period);
  }

  operations(period: DashboardPeriod): Observable<OperationsSummary> {
    return this.figure<OperationsSummary>('operations', period);
  }

  funnel(period: DashboardPeriod): Observable<CheckoutFunnel> {
    return this.figure<CheckoutFunnel>('funnel', period);
  }

  agentSales(period: DashboardPeriod): Observable<AgentSales> {
    return this.figure<AgentSales>('agent-sales', period);
  }

  private figure<T>(name: string, period: DashboardPeriod): Observable<T> {
    return this.http.get<T>(`${this.baseUrl}/api/dashboard/${name}`, {
      params: new HttpParams().set('period', period),
    });
  }
}
