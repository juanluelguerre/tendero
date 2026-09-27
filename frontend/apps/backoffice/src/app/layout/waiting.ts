import { computed, inject, Injectable, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { CultureStore } from '@tendero/shared-i18n';
import { catchError, forkJoin, map, of, type Observable } from 'rxjs';
import { AccountsService } from '../data-access/accounts.service';
import { CatalogService } from '../data-access/catalog.service';
import { InventoryService } from '../data-access/inventory.service';
import { KnowledgeService } from '../data-access/knowledge.service';
import { OrderingService } from '../data-access/ordering.service';
import { lowStockOf } from '../features/dashboard/figures';
import type { WaitingKind } from './navigation';

/**
 * How much work waits on each screen, for the rail's badges. Every figure is
 * one the dashboard already shows, read from the endpoint that already serves
 * it: the badge and the dashboard can never disagree about a number.
 *
 * Each figure fails alone. A badge that cannot load is no badge, never an error
 * in the navigation, because the rail is how somebody gets to the screen that
 * would explain the failure.
 */
@Injectable({ providedIn: 'root' })
export class Waiting {
  private readonly catalog = inject(CatalogService);
  private readonly knowledge = inject(KnowledgeService);
  private readonly ordering = inject(OrderingService);
  private readonly inventory = inject(InventoryService);
  private readonly accounts = inject(AccountsService);
  private readonly culture = inject(CultureStore);

  private readonly asked = signal(0);

  private readonly figures = rxResource({
    params: () => ({ culture: this.culture.active(), asked: this.asked() }),
    stream: ({ params }) =>
      forkJoin({
        drafts: quietly(this.catalog.list('draft', params.culture, 1, 1).pipe(map((page) => page.total))),
        claims: quietly(this.knowledge.claims().pipe(map((claims) => claims.length))),
        operations: quietly(this.ordering.operations('day')),
        lowStock: quietly(this.inventory.stock(params.culture).pipe(map((stock) => lowStockOf(stock.rows).length))),
        refusals: quietly(this.accounts.agentDenials('day').pipe(map((denials) => denials.total))),
      }),
  });

  /** The figure for a kind, or null when it has not loaded or failed. */
  readonly of = computed(() => {
    const figures = this.figures.value();
    return (kind: WaitingKind): number | null => {
      if (!figures) return null;
      switch (kind) {
        case 'toShip':
          return figures.operations?.toShip ?? null;
        case 'openReturns':
          return figures.operations?.openReturns ?? null;
        default:
          return figures[kind];
      }
    };
  });

  /** Asks again: after a navigation, which is when a count has usually moved. */
  refresh(): void {
    this.asked.update((n) => n + 1);
  }
}

function quietly<T>(source: Observable<T>): Observable<T | null> {
  return source.pipe(catchError(() => of(null)));
}
