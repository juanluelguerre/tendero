import { computed, inject, linkedSignal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
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
import type { ClaimForReview, ClaimSupportFilter } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { forkJoin } from 'rxjs';
import { CatalogService } from '../../data-access/catalog.service';
import { type ClaimDecision, KnowledgeService } from '../../data-access/knowledge.service';
import { readStatus } from '../../read-status';
import { type ActionVia, BackofficeUsage } from '../../usage';
import type { FilterOption } from '../../worklist/filter-chips/filter-chips';
import { withWorklistUrl } from '../../worklist/with-worklist-url';
import { extendResource } from '@ngrx/signals/resource';

/**
 * The claims queue's shared state (ADR 0047): the filter, the page and the open
 * claim in the URL, the queue with its products' names, and the decisions —
 * which the list and the pane both make. After a decision the queue is read
 * again (it is the server's) and the next claim opens, so a reviewer goes down
 * the list without losing their place.
 */
export const ClaimsReviewStore = signalStore(
  withState({ busy: null as string | null }),
  withWorklistUrl(),
  withProps(() => ({
    _knowledge: inject(KnowledgeService),
    _catalog: inject(CatalogService),
    _culture: inject(CultureStore),
    _usage: inject(BackofficeUsage),
    _labels: translateObjectSignal('claims.filter'),
  })),
  withComputed((store) => ({
    // Read in any case: a link saved before the filter took the enum's name said `all`.
    filterKey: computed<ClaimSupportFilter>(() =>
      store._params().get('support')?.toLowerCase() === 'all' ? 'All' : 'Weak',
    ),
    selectedId: computed(() => store._params().get('claim')),
  })),
  withProps((store) => {
    /**
     * The queue and the names of its products. A hundred products is the whole
     * laboratory catalogue (the scale rule); a shop past that would ask the
     * catalogue for the queue's products only.
     */
    const queue = extendResource(
      rxResource({
        params: () => ({ culture: store._culture.active(), support: store.filterKey(), page: store.page() }),
        stream: ({ params }) =>
          forkJoin({
            claims: store._knowledge.claims(params.support, params.page, store.pageSize),
            products: store._catalog.list(null, params.culture, 1, 100),
          }),
      }),
    );
    return { _queue: queue, ...readStatus(queue) };
  }),
  withLinkedState(({ _queue }) => ({
    /** The counts per filter, kept while the next page is on its way so the chips do not blink. */
    _counts: linkedSignal<ReadonlyMap<string, number> | undefined, ReadonlyMap<string, number> | null>({
      source: () => {
        const page = _queue.value()?.claims;
        return page && new Map(page.counts.map((entry) => [entry.support, entry.count]));
      },
      computation: (counts, previous) => counts ?? previous?.value ?? null,
    }),
  })),
  withComputed((store) => {
    const claims = computed(() => store._queue.value()?.claims.items ?? []);
    const names = computed(
      () => new Map((store._queue.value()?.products.items ?? []).map((product) => [product.productId, product.name])),
    );
    return {
      claims,
      _names: names,
      total: computed(() => store._queue.value()?.claims.total ?? 0),
      filters: computed<FilterOption[]>(() => {
        const labels = store._labels() as Record<string, string>;
        const counts = store._counts();
        return (['Weak', 'All'] as const).map((key) => ({
          key,
          label: labels[key] ?? key,
          count: counts?.get(key) ?? null,
        }));
      }),
      selected: computed(() => claims().find((claim) => claim.claimId === store.selectedId()) ?? null),
      /** The weak queue is empty and claims wait under All: the page points there rather than saying there is nothing. */
      othersWaiting: computed(() => store.filterKey() === 'Weak' && (store._counts()?.get('All') ?? 0) > 0),
    };
  }),
  withMethods((store) => {
    const navigate = store._navigate;
    const open = (claim: ClaimForReview) => navigate({ claim: claim.claimId });

    const settle = (claim: ClaimForReview) => {
      patchState(store, { busy: null });
      if (store.selectedId() === claim.claimId) {
        const list = store.claims();
        const next = list[list.findIndex((candidate) => candidate.claimId === claim.claimId) + 1];
        if (next) open(next);
        else navigate({ claim: null });
      }
      store._queue.reload();
    };

    return {
      /** The product a claim is about, by its name, or the start of its id when the catalogue has not answered. */
      productName(claim: ClaimForReview): string {
        return store._names().get(claim.productId) ?? claim.productId.slice(0, 8);
      },
      filterBy(key: string): void {
        navigate({ support: key === 'Weak' ? null : key, page: null });
      },
      open,
      close(): void {
        navigate({ claim: null });
      },
      decide(claim: ClaimForReview, decision: ClaimDecision, via: ActionVia): void {
        patchState(store, { busy: claim.claimId });

        store._knowledge
          .decide(claim.claimId, decision)
          .pipe(store._usage.reported('claims', decision === 'approve' ? 'claim_approve' : 'claim_reject', via))
          .subscribe({
            next: () => settle(claim),
            error: (failure: unknown) => {
              if (isSessionExpired(failure)) return;
              // A 409 means another reviewer decided it, or its text changed: the
              // queue is the server's, so asking again shows what is true now.
              settle(claim);
            },
          });
      },
    };
  }),
);
