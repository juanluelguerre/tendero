import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import type { ProductSummary } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { CatalogService } from '../../data-access/catalog.service';
import { readStatus } from '../../read-status';
import { type ActionVia, BackofficeUsage } from '../../usage';
import { valueOf } from '@tendero/shared-util';

/** The whole queue on one page: it is a laboratory's handful of drafts. */
const QueueSize = 100;

/**
 * The review queue's shared state (ADR 0047): the drafts, the one the URL has
 * open, and what is being published — which the list and the pane both read and
 * change. Publishing takes a draft out of the queue, and its pane with it.
 */
export const ReviewQueueStore = signalStore(
  withState({
    /** The drafts whose publishing is on its way. */
    publishing: new Set<string>() as ReadonlySet<string>,
    publishFailed: false,
  }),
  withProps(() => {
    const route = inject(ActivatedRoute);
    const catalog = inject(CatalogService);
    const culture = inject(CultureStore);
    const queue = rxResource({
      params: () => ({ culture: culture.active() }),
      stream: ({ params }) => catalog.list('Draft', params.culture, 1, QueueSize),
    });
    return {
      _router: inject(Router),
      _catalog: catalog,
      _usage: inject(BackofficeUsage),
      _params: toSignal(route.queryParamMap, { initialValue: route.snapshot.queryParamMap }),
      _queue: queue,
      _queueStatus: readStatus(queue),
      /** The drafts, less those published since the queue was read. */
      drafts: linkedSignal(() => valueOf(queue)?.items ?? []),
    };
  }),
  withComputed((store) => {
    const selectedCode = computed(() => store._params().get('product'));
    return {
      selectedCode,
      selected: computed(() => store.drafts().find((item) => item.code === selectedCode()) ?? null),
      loading: store._queueStatus.loading,
      failed: computed(() => store._queueStatus.failed() || store.publishFailed()),
    };
  }),
  withMethods((store) => {
    const navigate = (product: string | null) => void store._router.navigate([], { queryParams: { product }, queryParamsHandling: 'merge' });
    const settle = (productId: string) =>
      patchState(store, (state) => {
        const publishing = new Set(state.publishing);
        publishing.delete(productId);
        return { publishing };
      });

    return {
      open(item: ProductSummary): void {
        navigate(item.code);
      },
      close(): void {
        navigate(null);
      },
      publish(item: ProductSummary, via: ActionVia): void {
        patchState(store, (state) => ({ publishing: new Set(state.publishing).add(item.productId) }));

        store._catalog
          .publish(item.productId)
          .pipe(store._usage.reported('review', 'publish', via))
          .subscribe({
            next: () => {
              settle(item.productId);
              // It leaves the queue; the pane goes with it.
              store.drafts.update((items) => items.filter((candidate) => candidate.productId !== item.productId));
              if (store.selectedCode() === item.code) navigate(null);
            },
            error: (failure: unknown) => {
              settle(item.productId);
              if (isSessionExpired(failure)) return;
              patchState(store, { publishFailed: true });
            },
          });
      },
    };
  }),
);
