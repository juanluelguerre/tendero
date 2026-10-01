import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { catchError, forkJoin, map, of, type Observable } from 'rxjs';
import { AccountsService } from '../data-access/accounts.service';
import { CatalogService } from '../data-access/catalog.service';
import { InventoryService } from '../data-access/inventory.service';
import { KnowledgeService } from '../data-access/knowledge.service';
import { OrderingService } from '../data-access/ordering.service';
import type { WaitingKind } from './nav/navigation';
import { valueOf } from '@tendero/shared-util';

/**
 * How much work waits on each screen, for the rail's badges (ADR 0047: the rail
 * reads it and the shell refreshes it, so it is one store for the whole app).
 * Every figure is one the dashboard already shows, read from the endpoint that
 * already serves it: the badge and the dashboard can never disagree about a number.
 *
 * Each figure fails alone. A badge that cannot load is no badge, never an error
 * in the navigation, because the rail is how somebody gets to the screen that
 * would explain the failure.
 */
export const WaitingStore = signalStore(
  { providedIn: 'root' },
  withState({ asked: 0 }),
  withProps((store) => {
    const catalog = inject(CatalogService);
    const knowledge = inject(KnowledgeService);
    const ordering = inject(OrderingService);
    const inventory = inject(InventoryService);
    const accounts = inject(AccountsService);
    const culture = inject(CultureStore);
    return {
      _figures: rxResource({
        params: () => ({ culture: culture.active(), asked: store.asked() }),
        stream: ({ params }) =>
          forkJoin({
            drafts: quietly(catalog.list('Draft', params.culture, 1, 1).pipe(map((page) => page.total))),
            claims: quietly(knowledge.claims('All', 1, 1).pipe(map((queue) => queue.total))),
            operations: quietly(ordering.operations('Day')),
            lowStock: quietly(
              inventory
                .stock(params.culture, 'Restock')
                .pipe(map((stock) => stock.counts.find((count) => count.filter === 'Restock')?.count ?? 0)),
            ),
            refusals: quietly(accounts.agentDenials('Day').pipe(map((denials) => denials.total))),
          }),
      }),
    };
  }),
  withComputed((store) => ({
    /** The figure for a kind, or null when it has not loaded or failed. */
    of: computed(() => {
      const figures = valueOf(store._figures);
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
    }),
  })),
  withMethods((store) => ({
    /** Asks again: after a navigation, which is when a count has usually moved. */
    refresh(): void {
      patchState(store, (state) => ({ asked: state.asked + 1 }));
    },
  })),
);

function quietly<T>(source: Observable<T>): Observable<T | null> {
  return source.pipe(catchError(() => of(null)));
}
