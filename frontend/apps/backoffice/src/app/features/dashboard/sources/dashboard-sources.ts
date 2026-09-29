import { inject, Injectable } from '@angular/core';
import type {
  AgentDenialsSummary,
  AgentSales,
  CheckoutFunnel,
  OperationsSummary,
  SalesSeries,
  SalesSummary,
  StockList,
} from '@tendero/shared-api';
import { type Observable, shareReplay } from 'rxjs';
import { AccountsService } from '../../../data-access/accounts.service';
import { InventoryService } from '../../../data-access/inventory.service';
import { type DashboardPeriod, OrderingService } from '../../../data-access/ordering.service';

/**
 * The dashboard's reads, shared (R-9). Widgets are independent — each asks for
 * what it shows — but six on one screen must not mean ten requests: a read is
 * kept per endpoint, period and instant, so the widgets on the same figures
 * share one answer, and a new instant (a refresh) asks again. Provided by the
 * dashboard page, so leaving it forgets everything.
 */
@Injectable()
export class DashboardSources {
  private readonly ordering = inject(OrderingService);
  private readonly accounts = inject(AccountsService);
  private readonly inventory = inject(InventoryService);
  private readonly kept = new Map<string, Observable<unknown>>();
  private keptAt = Number.NaN;

  sales(period: DashboardPeriod, at: number): Observable<SalesSummary> {
    return this.shared(`sales:${period}`, at, () => this.ordering.sales(period));
  }

  series(period: DashboardPeriod, at: number): Observable<SalesSeries> {
    return this.shared(`series:${period}`, at, () => this.ordering.salesSeries(period));
  }

  funnel(period: DashboardPeriod, at: number): Observable<CheckoutFunnel> {
    return this.shared(`funnel:${period}`, at, () => this.ordering.funnel(period));
  }

  operations(period: DashboardPeriod, at: number): Observable<OperationsSummary> {
    return this.shared(`operations:${period}`, at, () => this.ordering.operations(period));
  }

  agentSales(period: DashboardPeriod, at: number): Observable<AgentSales> {
    return this.shared(`agentSales:${period}`, at, () => this.ordering.agentSales(period));
  }

  agentDenials(period: DashboardPeriod, at: number): Observable<AgentDenialsSummary> {
    return this.shared(`agentDenials:${period}`, at, () => this.accounts.agentDenials(period));
  }

  /** What to restock, by Inventory's own rule: read by the stock widget and by what is waiting for you. */
  stock(culture: string, at: number): Observable<StockList> {
    return this.shared(`stock:${culture}`, at, () => this.inventory.stock(culture, 'Restock'));
  }

  private shared<T>(key: string, at: number, read: () => Observable<T>): Observable<T> {
    // A new instant makes every kept answer old: forget them all at once.
    if (at !== this.keptAt) {
      this.kept.clear();
      this.keptAt = at;
    }

    let answer = this.kept.get(key) as Observable<T> | undefined;
    if (!answer) {
      // shareReplay resets on an error (RxJS 7), so a failed read is not kept:
      // the next widget or the next refresh asks again.
      answer = read().pipe(shareReplay({ bufferSize: 1, refCount: false }));
      this.kept.set(key, answer);
    }

    return answer;
  }
}
