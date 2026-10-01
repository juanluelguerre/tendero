import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { translateObjectSignal } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { OrderStatus, OrderSummary } from '@tendero/shared-api';
import { without } from '@tendero/shared-util';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { OrderingService, type OrderMove } from '../../data-access/ordering.service';
import { type ActionVia, BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { refusalOf } from '../../worklist/refusal';
import { keptCounts, statusChips } from '../../worklist/filter-chips/status-chips';
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

/** Twenty rows a page, as every list of work (R-7.2). */
export const PageSize = 20;

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
  withProps((store) => {
    const route = inject(ActivatedRoute);
    return {
      _route: route,
      _router: inject(Router),
      _ordering: inject(OrderingService),
      _usage: inject(BackofficeUsage),
      _labels: translateObjectSignal('orders.filter'),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      _query: toSignal(toObservable(store.text).pipe(debounceTime(250), distinctUntilChanged()), {
        initialValue: store.text(),
      }),
    };
  }),
  withComputed((store) => ({
    /** The status in the URL; none means the job's default, `all` means no filter. */
    statusKey: computed(() => store._params().get('status') ?? ToShip),
    selectedId: computed(() => store._params().get('order')),
    /** The page in the URL, as the filter is, so page three can be shared (R-7.2). */
    page: computed(() => Math.max(1, Number(store._params().get('page') ?? 1) || 1)),
  })),
  withProps((store) => {
    const list = extendResource(
      rxResource({
        params: () => ({ status: statusIn(store.statusKey()), text: store._query(), page: store.page() }),
        stream: ({ params }) => store._ordering.orders(params.status, params.text, params.page, PageSize),
      }),
    );
    /** The rows, replaced one by one as moves come back, until the list is asked again. */
    const orders = linkedSignal(() => list.value()?.orders ?? []);
    return {
      _list: list,
      _counts: keptCounts(() => list.value()?.counts),
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
    loading: computed(() => store._list.isLoading() && !store._list.hasValue()),
    failed: computed(() => store._list.status() === 'error'),
    /** Every status, always, then *All*: a fixed row that does not move under the pointer (`statusChips`). */
    filters: computed<FilterOption[]>(() =>
      statusChips(Statuses, store._labels() as Record<string, string>, store._counts()),
    ),
  })),
  withMethods((store) => {
    const navigate = (queryParams: Record<string, string | number | null>) =>
      void store._router.navigate([], { queryParams, queryParamsHandling: 'merge' });

    return {
      search(text: string): void {
        patchState(store, { text });
      },
      filterBy(status: string): void {
        navigate({ status: status === ToShip ? null : status, page: null });
      },
      go(page: number): void {
        navigate({ page: page <= 1 ? null : page });
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
              store.orders.update((current) => current.map((row) => (row.orderId === updated.orderId ? updated : row)));
              store.selected.update((row) => (row?.orderId === updated.orderId ? updated : row));
              patchState(store, { busy: null });
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
