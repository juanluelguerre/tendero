import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalMethod, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import { CultureStore } from '@tendero/shared-i18n';
import { StewardService } from '../../../data-access/steward.service';
import { readStatus } from '../../../read-status';

/** How many proposals the card shows before "and N more" (the mock-up). */
const Shown = 3;

/** A turn's proposals, and how many times the panel has said something was decided. */
export interface CardBatch {
  readonly ids: readonly string[];
  readonly revision: number;
}

/**
 * The card's own state (ADR 0047: one card uses it, so the card provides it):
 * the turn's proposals, read by their ids so the card says what became of them
 * since — another person may have decided some — and read again whenever the
 * panel's review decided something.
 */
export const ProposalCardStore = signalStore(
  withState<CardBatch>({ ids: [], revision: 0 }),
  withProps((store) => {
    const steward = inject(StewardService);
    const culture = inject(CultureStore);
    const batch = extendResource(
      rxResource({
        params: () =>
          store.ids().length > 0
            ? { ids: store.ids(), culture: culture.active(), revision: store.revision() }
            : undefined,
        stream: ({ params }) => steward.proposalsById(params.ids, params.culture),
      }),
    );
    return { _batch: batch, ...readStatus(batch) };
  }),
  withComputed((store) => {
    const items = computed(() => store._batch.value()?.items ?? []);
    return {
      count: computed(() => items().length),
      /** One turn proposes one kind of change: the first speaks for the batch. */
      topic: computed(() => items()[0]?.topic ?? ''),
      reason: computed(() => items()[0]?.reason ?? ''),
      shown: computed(() => items().slice(0, Shown)),
      more: computed(() => Math.max(0, items().length - Shown)),
      decided: computed(() => items().filter((item) => item.status !== 'Proposed').length),
    };
  }),
  withMethods((store) => ({
    /** Follows the card's inputs: the turn's ids, and the panel's count of decisions. */
    follow: signalMethod<CardBatch>((batch) => patchState(store, { ids: [...batch.ids], revision: batch.revision })),
  })),
);
