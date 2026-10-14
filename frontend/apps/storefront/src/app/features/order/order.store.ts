import { computed, inject, linkedSignal } from '@angular/core';
import { applyEach, form, max, min, required } from '@angular/forms/signals';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import type { OrderView, ReturnableLineView, ReturnReason, ReturnView } from '@tendero/shared-api';
import { RETURN_REASONS } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { refusalSentence } from '@tendero/shared-util';
import { OrderService } from '../../data-access/order.service';
import { type ReturnableLine, returnableLines } from './returnable';

/** One returnable line in the return form: whether it is coming back, how many, and why. */
export interface ReturnRow {
  readonly sku: string;
  /** How many may still come back: the quantity's ceiling. */
  readonly left: number;
  readonly picked: boolean;
  readonly quantity: number;
  readonly reason: ReturnReason;
}

interface OrderState {
  readonly order: OrderView | null;
  readonly returns: readonly ReturnView[];
  /** How many of each SKU the server says may still come back. */
  readonly _returnable: readonly ReturnableLineView[];
  readonly loading: boolean;
  readonly sending: boolean;
  readonly returnMessage: string | null;
  readonly returnOpened: boolean;
  /**
   * True only when this page was reached from checkout. The "thank you" belongs
   * to the moment, not to the URL: coming back to an order a week later should
   * not be congratulated for it.
   */
  readonly justPlaced: boolean;
}

/**
 * The order page's own state (ADR 0047): the order, the returns against it,
 * what may still come back, and the return being put together. The page
 * presents it — money in the reader's culture, the short id.
 *
 * The return form only appears once the order is **Delivered**. That is not a
 * UI preference: `ReturnRequest.Open` refuses anything else, and offering a
 * form the server will reject is how an interface teaches people not to trust
 * it.
 */
export const OrderStore = signalStore(
  withState<OrderState>({
    order: null,
    returns: [],
    _returnable: [],
    loading: true,
    sending: false,
    returnMessage: null,
    returnOpened: false,
    justPlaced: false,
  }),
  withProps(() => ({
    _orders: inject(OrderService),
    _culture: inject(CultureStore),
    _route: inject(ActivatedRoute),
    _router: inject(Router),
    _transloco: inject(TranslocoService),
  })),
  withComputed(({ order, _returnable }) => ({
    /** The lines the form offers: only what is left, never the whole order. */
    returnableLines: computed<ReturnableLine[]>(() => {
      const current = order();
      return current ? returnableLines(current, [..._returnable()]) : [];
    }),
    canReturn: computed(() => order()?.status === 'Delivered'),
  })),
  // The return is a Signal Form (ADR 0051): one row per line that may still come
  // back, started again whenever the server says what is left. A quantity is at
  // least one and at most what is left — invalid otherwise, never clamped behind
  // the shopper's back — and a picked line needs its reason.
  withProps(({ returnableLines }) => {
    const rows = linkedSignal<ReturnRow[]>(() =>
      returnableLines().map((line) => ({
        sku: line.sku,
        left: line.left,
        picked: false,
        quantity: 1,
        reason: RETURN_REASONS[0],
      })),
    );
    const returnForm = form(rows, (row) =>
      applyEach(row, (line) => {
        min(line.quantity, 1);
        max(line.quantity, ({ valueOf }) => valueOf(line.left));
        required(line.reason);
      }),
    );
    return { _returnRows: rows, returnForm };
  }),
  withComputed(({ _returnRows, returnForm, sending }) => {
    const pickedCount = computed(() => _returnRows().filter((row) => row.picked).length);
    return {
      pickedCount,
      /** Something picked, all of it valid, nothing on its way. */
      canSend: computed(() => pickedCount() > 0 && returnForm().valid() && !sending()),
    };
  }),
  withMethods((store) => {
    const load = async (): Promise<void> => {
      const id = store._route.snapshot.paramMap.get('id');
      if (!id) {
        patchState(store, { loading: false });
        return;
      }

      try {
        const detail = await store._orders.get(id, store._culture.active());
        patchState(store, { order: detail.order, returns: detail.returns, _returnable: detail.returnable });
      } catch {
        // A 404 is the honest answer to a URL somebody typed wrong, and the
        // template says so. Only the freshly-placed case has an order to keep.
        if (!store.justPlaced()) patchState(store, { order: null });
      } finally {
        patchState(store, { loading: false });
      }
    };

    return {
      _load: load,
      async requestReturn(): Promise<void> {
        const order = store.order();
        if (!order || !store.returnForm().valid()) return;

        patchState(store, { sending: true, returnMessage: null });
        try {
          const opened = await store._orders.requestReturn(
            order.orderId,
            {
              lines: store
                ._returnRows()
                .filter((row) => row.picked)
                .map((row) => ({ sku: row.sku, quantity: row.quantity, reason: row.reason, comment: null })),
            },
            store._culture.active(),
          );

          patchState(store, (state) => ({ returns: [opened, ...state.returns] }));
          store._returnRows.update((rows) => rows.map((row) => ({ ...row, picked: false })));
          // What is left to return changed, and only the server knows by how much.
          void load();
          patchState(store, { returnOpened: true, returnMessage: store._transloco.translate('order.returnOpened') });
        } catch (error) {
          const failure = error as { status?: number; error?: unknown };
          patchState(store, {
            returnOpened: false,
            returnMessage:
              failure.status === 409
                ? store._transloco.translate('order.returnClosed')
                : store._transloco.translate('order.returnFailed', {
                    reason: refusalSentence(failure.error),
                  }),
          });
        } finally {
          patchState(store, { sending: false });
        }
      },
    };
  }),
  withHooks({
    onInit(store) {
      const handed = store._router.getCurrentNavigation()?.extras.state as { order?: OrderView } | undefined;
      if (handed?.order) {
        // Checkout already has the order; asking for it again would be a round
        // trip to learn what we were just told.
        patchState(store, { order: handed.order, justPlaced: true, loading: false });
      }

      // Loaded anyway, and not only when the navigation state is empty. A
      // refresh has to work — a confirmation page that vanishes on F5 is a page
      // people screenshot instead — and the returns against the order only
      // exist server side.
      void store._load();
    },
  }),
);
