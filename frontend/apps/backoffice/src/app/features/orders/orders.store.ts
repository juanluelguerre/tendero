import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { translateObjectSignal } from '@jsverse/transloco';
import {
  patchState,
  signalStore,
  withComputed,
  withLinkedState,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import type { OrderStatus, OrderSummary } from '@tendero/shared-api';
import { without } from '@tendero/shared-util';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { OrderingService, type OrderMove } from '../../data-access/ordering.service';
import { type ActionVia, BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { refusalOf } from '../../worklist/refusal';
import { keptCounts, statusChips } from '../../worklist/filter-chips/status-chips';
import { withWorklistUrl } from '../../worklist/with-worklist-url';
import { readStatus } from '../../read-status';
import { OrderActions } from './order-moves';
import { extendResource } from '@ngrx/signals/resource';

/** The filter's statuses in the order a working day meets them: what is to ship first. */
const Statuses = [
  'Confirmed',
  'Pending',
  'PaymentAuthorized',
  'PaymentFailed',
  'Shipped',
  'Delivered',
  'Cancelled',
] as const satisfies readonly OrderStatus[];

/** The status the list opens on: the job this screen is for is shipping (R-0, B3). */
const ToShip: OrderStatus = 'Confirmed';

/**
 * The URL's status as the API's set, or none. A status typed by hand that the
 * shop does not have lists everything rather than asking for a 400: the address
 * is the person's, the set is the contract's.
 */
function statusIn(key: string): OrderStatus | null {
  return (Statuses as readonly string[]).includes(key) ? (key as OrderStatus) : null;
}

/**
 * The orders page's shared state (R-4.2, ADR 0047): what the list and the pane
 * both read and change — the filter, the text and the open order, which are in
 * the URL so a dashboard figure or the palette can open the list exactly there;
 * the rows, which a move made in the pane replaces in the list; and the moves
 * themselves. The pane's own state (the open order's detail) is the pane's store.
 */
export const OrdersStore = signalStore(
  withState(() => ({
    /** What is typed, from the URL's `q` the first time. */
    text: inject(ActivatedRoute).snapshot.queryParamMap.get('q') ?? '',
    /** The order a move is on its way for. */
    busy: null as string | null,
    /** The server's sentence per order it refused a move for; empty when it gave none. */
    refusal: {} as Record<string, string>,
  })),
  withWorklistUrl(),
  withProps((store) => ({
    _ordering: inject(OrderingService),
    _usage: inject(BackofficeUsage),
    _labels: translateObjectSignal('orders.filter'),
    _query: toSignal(toObservable(store.text).pipe(debounceTime(250), distinctUntilChanged()), {
      initialValue: store.text(),
    }),
  })),
  withComputed((store) => ({
    /** The status in the URL; none means the job's default, `all` means no filter. */
    statusKey: computed(() => store._params().get('status') ?? ToShip),
    selectedId: computed(() => store._params().get('order')),
  })),
  withProps((store) => {
    const list = extendResource(
      rxResource({
        params: () => ({ status: statusIn(store.statusKey()), text: store._query(), page: store.page() }),
        stream: ({ params }) => store._ordering.orders(params.status, params.text, params.page, store.pageSize),
      }),
    );
    return { _list: list, _counts: keptCounts(() => list.value()?.counts) };
  }),
  // State a move changes and a new answer replaces: protected like the rest, changed with patchState.
  withLinkedState((store) => {
    /** The rows, replaced one by one as moves come back, until the list is asked again. */
    const orders = linkedSignal(() => store._list.value()?.orders ?? []);
    return {
      orders,
      /** The open order's row, kept while a move takes it out of the filtered list. */
      selected: linkedSignal<OrderSummary | null, OrderSummary | null>({
        source: () => orders().find((order) => order.orderId === store.selectedId()) ?? null,
        computation: (row, previous) =>
          row ?? (previous?.value?.orderId === store.selectedId() ? previous.value : null),
      }),
    };
  }),
  withComputed((store) => ({
    /** How many the filter and the search hold, not the page: the pager says it. */
    total: computed(() => store._list.value()?.total ?? 0),
    // As every other list says it: an expired session is the door's to explain, not a failure.
    ...readStatus(store._list),
    /** Every status, always, then *All*: a fixed row that does not move under the pointer (`statusChips`). */
    filters: computed<FilterOption[]>(() =>
      statusChips(Statuses, store._labels() as Record<string, string>, store._counts()),
    ),
  })),
  withMethods((store) => {
    const navigate = store._navigate;

    return {
      search(text: string): void {
        patchState(store, { text });
      },
      filterBy(status: string): void {
        navigate({ status: status === ToShip ? null : status, page: null });
      },
      open(order: OrderSummary): void {
        navigate({ order: order.orderId });
      },
      close(): void {
        navigate({ order: null });
      },
      apply(order: OrderSummary, move: OrderMove, via: ActionVia, reason?: string): void {
        patchState(store, (state) => ({ busy: order.orderId, refusal: without(state.refusal, order.orderId) }));

        store._ordering
          .move(order.orderId, move, reason)
          .pipe(store._usage.reported('orders', OrderActions[move], via))
          .subscribe({
            next: (updated) => {
              patchState(store, (state) => ({
                busy: null,
                orders: state.orders.map((row) => (row.orderId === updated.orderId ? updated : row)),
                selected: state.selected?.orderId === updated.orderId ? updated : state.selected,
              }));
            },
            error: (error: unknown) => {
              patchState(store, (state) => ({
                busy: null,
                refusal: { ...state.refusal, [order.orderId]: refusalOf(error) },
              }));
            },
          });
      },
    };
  }),
);
