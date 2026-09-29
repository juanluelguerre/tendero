import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import type { SearchEvaluation } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import type { Subscription } from 'rxjs';
import { SearchService } from '../../data-access/search.service';
import { toView } from './evaluation-view';

type EvaluationState =
  | { status: 'idle' }
  | { status: 'running' }
  | { status: 'ready'; result: SearchEvaluation }
  | { status: 'failed' };

/**
 * The search evaluation's state (ADR 0047). It runs the whole golden set, so it
 * never runs on opening the screen — only when somebody asks — and a second run
 * cancels the first rather than racing it.
 */
export const SearchEvaluationStore = signalStore(
  withState<{ evaluation: EvaluationState }>({ evaluation: { status: 'idle' } }),
  withProps(() => ({
    _search: inject(SearchService),
    _inFlight: { subscription: undefined as Subscription | undefined },
  })),
  withComputed(({ evaluation }) => {
    const result = computed(() => {
      const current = evaluation();
      return current.status === 'ready' ? current.result : null;
    });
    return {
      status: computed(() => evaluation().status),
      result,
      cultures: computed(() => (result()?.cultures ?? []).map(toView)),
    };
  }),
  withMethods((store) => ({
    run(): void {
      store._inFlight.subscription?.unsubscribe();
      patchState(store, { evaluation: { status: 'running' } });
      store._inFlight.subscription = store._search.evaluation().subscribe({
        next: (result) => patchState(store, { evaluation: { status: 'ready', result } }),
        error: (failure: unknown) => {
          if (isSessionExpired(failure)) return;
          patchState(store, { evaluation: { status: 'failed' } });
        },
      });
    },
  })),
  withHooks({
    onDestroy(store) {
      store._inFlight.subscription?.unsubscribe();
    },
  }),
);
