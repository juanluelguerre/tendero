import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { BulkApprovalOutcome, ClaimForReview } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { KnowledgeService } from '../../../data-access/knowledge.service';
import { readStatus } from '../../../read-status';

/** A row of the review: the claim, whether it may go together with others, and what became of it. */
export interface ClaimRow {
  readonly claim: ClaimForReview;
  /** Still waiting and well supported: a weak claim is never approved together. */
  readonly tickable: boolean;
  readonly ticked: boolean;
  readonly outcome: BulkApprovalOutcome | null;
}

/**
 * The bulk review's own state (ADR 0047: one dialog uses it, so the dialog
 * provides it). A product's waiting claims, every one ticked but the weakly
 * supported — the server's rule, sent as `isWeak`, never a copy of the line here
 * — and each row's outcome once the server answered.
 */
export const ClaimsBulkReviewStore = signalStore(
  withState({
    productId: null as string | null,
    untickedIds: [] as readonly string[],
    outcomes: {} as Readonly<Record<string, BulkApprovalOutcome>>,
    busy: false,
  }),
  withProps((store) => {
    const knowledge = inject(KnowledgeService);
    const claims = extendResource(
      rxResource({
        params: () => store.productId() ?? undefined,
        stream: ({ params }) => knowledge.claimsOf(params),
      }),
    );
    return { _knowledge: knowledge, _claims: claims, ...readStatus(claims) };
  }),
  withComputed((store) => {
    const rows = computed<ClaimRow[]>(() =>
      (store._claims.value()?.items ?? []).map((claim) => {
        const outcome = store.outcomes()[claim.claimId] ?? null;
        const tickable = !outcome && !claim.isWeak;
        return { claim, tickable, ticked: tickable && !store.untickedIds().includes(claim.claimId), outcome };
      }),
    );
    const selected = computed(() => rows().filter((row) => row.ticked).length);
    return {
      rows,
      /** Every claim the review shows. */
      count: computed(() => rows().length),
      /** How many may go together: well supported and still waiting. */
      together: computed(() => rows().filter((row) => row.tickable).length),
      weak: computed(() => rows().filter((row) => !row.outcome && row.claim.isWeak).length),
      firstWeak: computed(() => rows().find((row) => !row.outcome && row.claim.isWeak)?.claim ?? null),
      selected,
      canApprove: computed(() => selected() > 0 && !store.busy()),
    };
  }),
  withMethods((store) => ({
    /** A product's claims, every row ticked again. */
    load(productId: string): void {
      patchState(store, { productId, untickedIds: [], outcomes: {}, busy: false });
      store._claims.reload();
    },
    toggle(claimId: string): void {
      patchState(store, (state) => ({
        untickedIds: state.untickedIds.includes(claimId)
          ? state.untickedIds.filter((other) => other !== claimId)
          : [...state.untickedIds, claimId],
      }));
    },
    /** Approves the ticked; `answered` runs once the server said what became of each. */
    approve(answered?: () => void): void {
      if (!store.canApprove()) return;
      patchState(store, { busy: true });
      store._knowledge
        .approveMany(
          store
            .rows()
            .filter((row) => row.ticked)
            .map((row) => row.claim.claimId),
        )
        .subscribe({
          next: (answer) => {
            patchState(store, (state) => ({
              busy: false,
              outcomes: {
                ...state.outcomes,
                ...Object.fromEntries(answer.rows.map((row) => [row.claimId, row.outcome])),
              },
            }));
            answered?.();
          },
          error: (failure: unknown) => {
            patchState(store, { busy: false });
            if (isSessionExpired(failure)) return;
            store._claims.reload();
          },
        });
    },
  })),
);
