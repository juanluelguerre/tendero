import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';
import { bySkuLowestFirst } from '../figures';

const Shown = 5;

/**
 * The stock's state (ADR 0047): what to restock and what is sold out, as
 * Inventory counts them by its own rule, and the lowest first. Stock is now,
 * not a window: no period here.
 */
export const StockStore = signalStore(
  withProps(() => {
    const sources = inject(DashboardSources);
    const culture = inject(CultureStore);
    return {
      stock: refreshedResource({ params: () => ({ culture: culture.active() }), stream: (params, at) => sources.stock(params.culture, at) }),
    };
  }),
  withComputed((store) => {
    const counts = computed(() => store.stock.value()?.counts ?? []);
    return {
      restock: computed(() => counts().find((count) => count.filter === 'Restock')?.count ?? 0),
      soldOut: computed(() => counts().find((count) => count.filter === 'SoldOut')?.count ?? 0),
      lowest: computed(() => bySkuLowestFirst(store.stock.value()?.rows ?? []).slice(0, Shown)),
    };
  }),
);
