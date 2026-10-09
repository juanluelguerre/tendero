import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { PictureDoubtDecision, PictureDoubtView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { forkJoin } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { KnowledgeService } from '../../data-access/knowledge.service';
import { readStatus } from '../../read-status';
import { BackofficeUsage } from '../../usage';
import { withWorklistUrl } from '../../worklist/with-worklist-url';

/**
 * The doubts about pictures and the decisions on them (phase 13a, I-4d): the
 * page in the URL, the queue with its products' names, and a decision that is
 * the server's to record — after which the queue is read again.
 */
export const PictureDoubtsStore = signalStore(
  withState({ busy: null as string | null }),
  withWorklistUrl(),
  withProps(() => ({
    _knowledge: inject(KnowledgeService),
    _catalog: inject(CatalogService),
    _culture: inject(CultureStore),
    _usage: inject(BackofficeUsage),
  })),
  withProps((store) => {
    // The queue and the names of its products: a hundred products is the whole laboratory catalogue (the scale rule).
    const queue = extendResource(
      rxResource({
        params: () => ({ culture: store._culture.active(), page: store.page() }),
        stream: ({ params }) =>
          forkJoin({
            doubts: store._knowledge.pictureDoubts(params.page, store.pageSize, params.culture),
            products: store._catalog.list(null, params.culture, 1, 100),
          }),
      }),
    );
    return { _queue: queue, ...readStatus(queue) };
  }),
  withComputed((store) => ({
    doubts: computed(() => store._queue.value()?.doubts.items ?? []),
    total: computed(() => store._queue.value()?.doubts.total ?? 0),
    _names: computed(
      () => new Map((store._queue.value()?.products.items ?? []).map((product) => [product.productId, product.name])),
    ),
  })),
  withMethods((store) => ({
    /** The product a doubt's picture is of, by its name, or the start of its id when the catalogue has not answered. */
    productName(doubt: PictureDoubtView): string {
      return store._names().get(doubt.productId) ?? doubt.productId.slice(0, 8);
    },
    decide(doubt: PictureDoubtView, decision: PictureDoubtDecision): void {
      patchState(store, { busy: doubt.doubtId });

      const settle = () => {
        patchState(store, { busy: null });
        store._queue.reload();
      };

      store._knowledge
        .decideDoubt(doubt.doubtId, decision)
        .pipe(store._usage.reported('doubts', 'doubt_decide', 'row'))
        .subscribe({
          next: settle,
          error: (failure: unknown) => {
            if (isSessionExpired(failure)) return;
            // A 409 means somebody else decided it, or the picture was read again:
            // the queue is the server's, so asking again shows what is true now.
            settle();
          },
        });
    },
  })),
);
