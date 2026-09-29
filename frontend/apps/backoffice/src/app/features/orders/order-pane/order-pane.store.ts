import { effect, inject, untracked } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withHooks, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { OrderingService } from '../../../data-access/ordering.service';
import { OrdersStore } from '../orders.store';

/**
 * The pane's own state (ADR 0047): the open order's detail — its address and
 * lines — which only the pane reads. What the pane shares with the list (which
 * order is open, its row, the moves) is the page's store, injected here; the
 * page's store knows nothing of the pane.
 */
export const OrderPaneStore = signalStore(
  withProps(() => {
    const orders = inject(OrdersStore);
    const ordering = inject(OrderingService);
    const culture = inject(CultureStore);
    return {
      _orders: orders,
      detail: rxResource({
        params: () => {
          const id = orders.selectedId();
          return id ? { id, culture: culture.active() } : undefined;
        },
        stream: ({ params }) => ordering.order(params.id, params.culture),
      }),
    };
  }),
  withHooks({
    onInit(store) {
      // A move changes the open order's status: its detail is asked again,
      // keeping what it shows until the answer arrives.
      let seen = untracked(() => store._orders.selected());
      effect(() => {
        const row = store._orders.selected();
        const moved = row !== null && seen !== null && row.orderId === seen.orderId && row.status !== seen.status;
        seen = row;
        if (moved) untracked(() => store.detail.reload());
      });
    },
  }),
);
