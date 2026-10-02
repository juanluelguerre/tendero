import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { ProposalDecisionsResponse, ProposalDecisionView, ProposalView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import type { Observable } from 'rxjs';
import { StewardService } from '../../../data-access/steward.service';
import { readStatus } from '../../../read-status';
import { BackofficeUsage } from '../../../usage';

/** A row of the review: the proposal, whether it is ticked, and what its decision did. */
export interface ReviewRow {
  readonly proposal: ProposalView;
  /** Still waiting for a person, and so decidable here. */
  readonly open: boolean;
  readonly ticked: boolean;
  readonly outcome: ProposalDecisionView | null;
}

/**
 * The review's own state (ADR 0047: one dialog uses it, so the dialog provides
 * it). A batch is read by its ids — whatever became of each since the turn — and
 * every proposal still waiting is ticked: unticking is the person's act, never
 * the default (spec, decision 4). Each row then carries its own decision's
 * result, as the server answered it, row by row.
 */
export const ProposalReviewStore = signalStore(
  withState({
    ids: [] as readonly string[],
    untickedIds: [] as readonly string[],
    outcomes: {} as Readonly<Record<string, ProposalDecisionView>>,
    busy: false,
  }),
  withProps((store) => {
    const steward = inject(StewardService);
    const culture = inject(CultureStore);
    const batch = extendResource(
      rxResource({
        params: () => (store.ids().length > 0 ? { ids: store.ids(), culture: culture.active() } : undefined),
        stream: ({ params }) => steward.proposalsById(params.ids, params.culture),
      }),
    );
    return { _steward: steward, _usage: inject(BackofficeUsage), _batch: batch, ...readStatus(batch) };
  }),
  withComputed((store) => {
    const rows = computed<ReviewRow[]>(() =>
      (store._batch.value()?.items ?? []).map((proposal) => {
        const outcome = store.outcomes()[proposal.id] ?? null;
        const open = proposal.status === 'Proposed' && !outcome;
        return { proposal, open, ticked: open && !store.untickedIds().includes(proposal.id), outcome };
      }),
    );
    const selected = computed(() => rows().filter((row) => row.ticked).length);
    return {
      rows,
      /** The first proposal speaks for the batch: one turn, one model, one prompt. */
      first: computed(() => rows()[0]?.proposal ?? null),
      waiting: computed(() => rows().filter((row) => row.open).length),
      selected,
      unticked: computed(() => rows().filter((row) => row.open && !row.ticked).length),
      canApprove: computed(() => selected() > 0 && !store.busy()),
    };
  }),
  withMethods((store) => {
    /** Sends a decision; `answered` runs once the server said what became of each row. */
    const decide = (decision: Observable<ProposalDecisionsResponse>, answered?: () => void) => {
      patchState(store, { busy: true });
      decision.subscribe({
        next: (answer) => {
          patchState(store, (state) => ({
            busy: false,
            outcomes: { ...state.outcomes, ...Object.fromEntries(answer.rows.map((row) => [row.id, row])) },
          }));
          answered?.();
        },
        error: (failure: unknown) => {
          patchState(store, { busy: false });
          if (isSessionExpired(failure)) return;
          // The batch is the server's: reading it again shows what each row is now.
          store._batch.reload();
        },
      });
    };

    return {
      /** A new batch: every row ticked again, no result yet. */
      load(ids: readonly string[]): void {
        patchState(store, { ids: [...ids], untickedIds: [], outcomes: {}, busy: false });
        store._batch.reload();
      },
      toggle(id: string): void {
        patchState(store, (state) => ({
          untickedIds: state.untickedIds.includes(id)
            ? state.untickedIds.filter((other) => other !== id)
            : [...state.untickedIds, id],
        }));
      },
      approve(answered?: () => void): void {
        if (!store.canApprove()) return;
        store._usage.proposalsReviewed('proposals', {
          proposed: store.waiting(),
          approved: store.selected(),
          rejected: 0,
          unticked: store.unticked(),
        });
        decide(
          store._steward.approve(
            store
              .rows()
              .filter((row) => row.ticked)
              .map((row) => row.proposal.id),
          ),
          answered,
        );
      },
      rejectUnticked(reason: string, answered?: () => void): void {
        const ids = store
          .rows()
          .filter((row) => row.open && !row.ticked)
          .map((row) => row.proposal.id);
        if (ids.length === 0 || !reason.trim() || store.busy()) return;
        store._usage.proposalsReviewed('proposals', {
          proposed: store.waiting(),
          approved: 0,
          rejected: ids.length,
          unticked: store.unticked(),
        });
        decide(store._steward.reject(ids, reason.trim()), answered);
      },
    };
  }),
);
