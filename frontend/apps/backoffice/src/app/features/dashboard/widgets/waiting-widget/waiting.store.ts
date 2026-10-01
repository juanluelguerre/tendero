import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { CatalogService } from '../../../../data-access/catalog.service';
import { KnowledgeService } from '../../../../data-access/knowledge.service';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { bySkuLowestFirst } from '../figures';
import { valueOf } from '@tendero/shared-util';

/** How many claims the widget names: the weakest first, the queue's own order. */
export const ClaimsShown = 3;

/**
 * What is waiting for the shopkeeper, as state (ADR 0047): drafts to publish,
 * claims to review and stock to restock — three reads that already had their
 * endpoints, each leading to the screen that acts on it.
 */
export const WaitingStore = signalStore(
  withProps(() => {
    const catalog = inject(CatalogService);
    const knowledge = inject(KnowledgeService);
    const sources = inject(DashboardSources);
    const culture = inject(CultureStore);
    return {
      drafts: refreshedResource({
        params: () => ({ culture: culture.active() }),
        stream: (params) => catalog.list('Draft', params.culture, 1, 1),
      }),
      claims: refreshedResource({ params: () => ({}), stream: () => knowledge.claims('All', 1, ClaimsShown) }),
      stock: refreshedResource({
        params: () => ({ culture: culture.active() }),
        stream: (params, at) => sources.stock(params.culture, at),
      }),
    };
  }),
  withComputed((store) => ({
    /** The server's first page is the weakest first: the queue's own order. */
    weakestClaims: computed(() => valueOf(store.claims)?.items ?? []),
    /**
     * The SKUs to restock, by the server's rule, and how many (R-7.1). Per SKU,
     * across warehouses: counted per warehouse row, a SKU stocked in only one
     * warehouse read as empty in the other, and 144 "low" rows said nothing a
     * shopkeeper could act on.
     */
    lowStock: computed(() => bySkuLowestFirst(valueOf(store.stock)?.rows ?? [])),
    restockCount: computed(() => valueOf(store.stock)?.counts.find((count) => count.filter === 'Restock')?.count ?? 0),
  })),
);
