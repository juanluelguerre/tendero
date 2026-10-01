import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { translateObjectSignal } from '@jsverse/transloco';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { ReturnView } from '@tendero/shared-api';
import { without } from '@tendero/shared-util';
import { OrderingService, type ReturnDecision } from '../../data-access/ordering.service';
import { readStatus } from '../../read-status';
import { type ActionVia, BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { refusalOf } from '../../worklist/refusal';
import { ReturnActions } from './return-decisions';
import { extendResource } from '@ngrx/signals/resource';

/** The statuses a return waits in, in the order a working day meets them. */
const Statuses = ['Requested', 'Approved', 'Received'] as const;

/** What the list opens on: deciding what was asked for. */
const Waiting = 'Requested';

/**
 * The returns' shared state (ADR 0047): the requests, the filter and the open
 * one in the URL, and the decisions — which the list and the pane both make and
 * both show. A decision replaces its row with what the server answered.
 */
export const ReturnsStore = signalStore(
  withState({
    /** The return a decision is on its way for. */
    busy: null as string | null,
    /** The server's sentence per return it refused a decision for; empty when it gave none. */
    refusal: {} as Record<string, string>,
  }),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const ordering = inject(OrderingService);
    const queue = extendResource(rxResource({ stream: () => ordering.returns() }));
    return {
      _router: inject(Router),
      _ordering: ordering,
      _usage: inject(BackofficeUsage),
      _labels: translateObjectSignal('returns.filter'),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      ...readStatus(queue),
      /** The requests, replaced one by one as decisions come back. */
      requests: linkedSignal(() => queue.value() ?? []),
    };
  }),
  withComputed((store) => {
    const statusKey = computed(() => store._params().get('status') ?? Waiting);
    const selectedId = computed(() => store._params().get('return'));
    return {
      statusKey,
      selectedId,
      shown: computed(() =>
        statusKey() === 'all' ? store.requests() : store.requests().filter((request) => request.status === statusKey()),
      ),
      filters: computed<FilterOption[]>(() => {
        const labels = store._labels() as Record<string, string>;
        const count = (status: string) => store.requests().filter((request) => request.status === status).length;
        return [
          ...Statuses.map((status) => ({
            key: status as string,
            label: labels[status] ?? status,
            count: count(status),
          })),
          { key: 'all', label: labels['all'] ?? 'all', count: store.requests().length },
        ];
      }),
      selected: computed(() => store.requests().find((request) => request.returnId === selectedId()) ?? null),
    };
  }),
  withMethods((store) => {
    const navigate = (queryParams: Record<string, string | null>) =>
      void store._router.navigate([], { queryParams, queryParamsHandling: 'merge' });

    return {
      filterBy(status: string): void {
        navigate({ status: status === Waiting ? null : status });
      },
      open(request: ReturnView): void {
        navigate({ return: request.returnId });
      },
      close(): void {
        navigate({ return: null });
      },
      decide(request: ReturnView, decision: ReturnDecision, via: ActionVia, reason?: string): void {
        patchState(store, (state) => ({ busy: request.returnId, refusal: without(state.refusal, request.returnId) }));

        store._ordering
          .decide(request.returnId, decision, reason)
          .pipe(store._usage.reported('returns', ReturnActions[decision], via))
          .subscribe({
            next: (updated) => {
              store.requests.update((current) =>
                current.map((row) => (row.returnId === updated.returnId ? updated : row)),
              );
              patchState(store, { busy: null });
            },
            error: (error: unknown) => {
              patchState(store, (state) => ({
                busy: null,
                refusal: { ...state.refusal, [request.returnId]: refusalOf(error) },
              }));
            },
          });
      },
    };
  }),
);
