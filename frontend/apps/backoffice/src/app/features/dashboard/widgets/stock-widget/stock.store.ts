import { computed, inject } from '@angular/core';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { refreshedResource } from '../../sources/dashboard-refresh';
import { DashboardSources } from '../../sources/dashboard-sources';

/**
 * The stock's state (ADR 0047): what to restock and what is sold out, and — at
 * the shop's pace over the last four weeks (R-9, D-7) — what runs out in fewer
 * than fourteen days, what did not sell at all, and the closest to running out.
 * Coverage is not a window, so the widget takes no period and asks for the
 * default one; the period's sell-through is a widget of its own.
 */
export const StockStore = signalStore(
  withProps(() => {
    const sources = inject(DashboardSources);
    return {
      stock: refreshedResource({ params: () => ({ period: 'Week' as const }), stream: (params, at) => sources.stockHealth(params.period, at) }),
    };
  }),
  withComputed((store) => ({
    restock: computed(() => store.stock.value()?.toRestock ?? 0),
    soldOut: computed(() => store.stock.value()?.soldOut ?? 0),
    soon: computed(() => store.stock.value()?.underTwoWeeks ?? 0),
    noSales: computed(() => store.stock.value()?.noSales ?? 0),
    closest: computed(() => store.stock.value()?.closest ?? []),
    warehouses: computed(() => store.stock.value()?.warehouses ?? []),
  })),
);
