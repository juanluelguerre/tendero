import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { patchState, signalStore, withComputed, withMethods, withProps, withState } from '@ngrx/signals';
import { extendResource } from '@ngrx/signals/resource';
import type { BulkApprovalOutcome, ClaimForReview } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { KnowledgeService } from '../../../data-access/knowledge.service';
import { readStatus } from '../../../read-status';
import { BackofficeUsage } from '../../../usage';

/** A row of the review: the claim, whether it may go together with others, and what became of it. */
export interface ClaimRow {
  readonly claim: ClaimForReview;
  /** Still waiting and free to go with others: a weak claim, or one read from a picture, never is. */
  readonly tickable: boolean;
  readonly ticked: boolean;
  readonly outcome: BulkApprovalOutcome | null;
}

/**
 * The bulk review's own state (ADR 0047: one dialog uses it, so the dialog
 * provides it). A product's waiting claims, every one ticked but those decided on
 * their own — the weakly supported, and the ones read from a picture, whose
 * support is a model's sentence held to a model's description. The server's
 * rule, sent as `decidedAlone`, never a copy of it here — and each row's outcome
 * once the server answered.
 */
export const ClaimsBulkReviewStore = signalStore(
  withState({
    productId: null as string | null,
    untickedIds: [] as readonly string[],
    outcomes: {} as Readonly<Record<string, BulkApprovalOutcome>>,
    busy: false,
    /** The last approval did not reach the server, or it failed there. */
    approveFailed: false,
  }),
  withProps((store) => {
    const knowledge = inject(KnowledgeService);
    const culture = inject(CultureStore);
    const claims = extendResource(
      rxResource({
        params: () => {
          const productId = store.productId();
          return productId ? { productId, culture: culture.active() } : undefined;
        },
        stream: ({ params }) => knowledge.claimsOf(params.productId, params.culture),
      }),
    );
    return { _knowledge: knowledge, _usage: inject(BackofficeUsage), _claims: claims, ...readStatus(claims) };
  }),
  withComputed((store) => {
    // The rows that can go together first, those decided on their own after them, as the mock-up draws it.
    const claims = computed(() =>
      [...(store._claims.value()?.items ?? [])].sort((a, b) => Number(!!a.decidedAlone) - Number(!!b.decidedAlone)),
    );
    const rows = computed<ClaimRow[]>(() =>
      claims().map((claim) => {
        const outcome = store.outcomes()[claim.claimId] ?? null;
        const tickable = !outcome && !claim.decidedAlone;
        return { claim, tickable, ticked: tickable && !store.untickedIds().includes(claim.claimId), outcome };
      }),
    );
    const selected = computed(() => rows().filter((row) => row.ticked).length);
    return {
      rows,
      /** Every claim the review shows. */
      count: computed(() => rows().length),
      /** Every claim the product has waiting: more than the review shows past its hundred. */
      total: computed(() => store._claims.value()?.total ?? 0),
      approved: computed(() => rows().filter((row) => row.outcome === 'Approved').length),
      /** How many may go together: still waiting, and not decided on their own. */
      together: computed(() => rows().filter((row) => row.tickable).length),
      /** How many are opened one by one: weakly supported, or read from a picture. */
      alone: computed(() => rows().filter((row) => !row.outcome && row.claim.decidedAlone).length),
      firstAlone: computed(() => rows().find((row) => !row.outcome && row.claim.decidedAlone)?.claim ?? null),
      selected,
      canApprove: computed(() => selected() > 0 && !store.busy()),
    };
  }),
  withMethods((store) => ({
    /** A product's claims, every row ticked again. */
    load(productId: string): void {
      patchState(store, { productId, untickedIds: [], outcomes: {}, busy: false, approveFailed: false });
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
      // Those decided on their own were proposed too, and could not go together: they count as shown, not as unticked.
      const [proposed, unticked] = [store.together() + store.alone(), store.together() - store.selected()];
      patchState(store, { busy: true, approveFailed: false });
      store._knowledge
        .approveMany(
          store
            .rows()
            .filter((row) => row.ticked)
            .map((row) => row.claim.claimId),
        )
        .subscribe({
          next: (answer) => {
            store._usage.proposalsReviewed('claims', {
              proposed,
              approved: answer.rows.filter((row) => row.outcome === 'Approved').length,
              rejected: 0,
              unticked,
            });
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
            if (isSessionExpired(failure)) return patchState(store, { busy: false });
            // Said, then read again: what the server did decide shows on its row.
            patchState(store, { busy: false, approveFailed: true });
            store._claims.reload();
          },
        });
    },
  })),
);
