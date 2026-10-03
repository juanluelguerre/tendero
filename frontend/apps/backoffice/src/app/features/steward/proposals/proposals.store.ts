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
import { extendResource } from '@ngrx/signals/resource';
import type { ProposalDecisionsResponse, ProposalFilter, ProposalView } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { StewardService } from '../../../data-access/steward.service';
import { readStatus } from '../../../read-status';
import { BackofficeUsage } from '../../../usage';
import type { FilterOption } from '../../../worklist/filter-chips/filter-chips';
import { withWorklistUrl } from '../../../worklist/with-worklist-url';

/** The chips, in the order a person sorts their work (the spec, *The Proposals list*). */
export const ProposalFilters: readonly ProposalFilter[] = ['ToReview', 'Applied', 'Rejected', 'Failed', 'All'];

/**
 * The Proposals list's shared state (ADR 0047): the filter, the page and the
 * open proposal in the URL, the rows with every chip's count from the server,
 * and the decisions the list and the pane both make. After a decision the list
 * is read again — it is the server's — and the next proposal opens, so a
 * reviewer goes down the list without losing their place.
 */
export const ProposalsStore = signalStore(
  withState({ busy: null as string | null }),
  withWorklistUrl(),
  withProps(() => ({
    _steward: inject(StewardService),
    _culture: inject(CultureStore),
    _usage: inject(BackofficeUsage),
    _labels: translateObjectSignal('proposals.filter'),
  })),
  withComputed((store) => ({
    /** Read in any case; anything that is not a filter is the work waiting. */
    filterKey: computed<ProposalFilter>(() => {
      const asked = store._params().get('filter')?.toLowerCase();
      return ProposalFilters.find((filter) => filter.toLowerCase() === asked) ?? 'ToReview';
    }),
    selectedId: computed(() => store._params().get('proposal')),
  })),
  withProps((store) => {
    const list = extendResource(
      rxResource({
        params: () => ({ culture: store._culture.active(), filter: store.filterKey(), page: store.page() }),
        stream: ({ params }) => store._steward.proposals(params.filter, params.culture, params.page, store.pageSize),
      }),
    );
    return { _list: list, ...readStatus(list) };
  }),
  withLinkedState(({ _list }) => ({
    /** The counts per filter, kept while the next page is on its way so the chips do not blink. */
    _counts: linkedSignal<ReadonlyMap<string, number> | undefined, ReadonlyMap<string, number> | null>({
      source: () => {
        const page = _list.value();
        return page && new Map(page.counts.map((entry) => [entry.filter, entry.count]));
      },
      computation: (counts, previous) => counts ?? previous?.value ?? null,
    }),
  })),
  withComputed((store) => {
    const proposals = computed(() => store._list.value()?.items ?? []);
    return {
      proposals,
      total: computed(() => store._list.value()?.total ?? 0),
      filters: computed<FilterOption[]>(() => {
        const labels = store._labels() as Record<string, string>;
        const counts = store._counts();
        return ProposalFilters.map((key) => ({ key, label: labels[key] ?? key, count: counts?.get(key) ?? null }));
      }),
      selected: computed(() => proposals().find((proposal) => proposal.id === store.selectedId()) ?? null),
    };
  }),
  withMethods((store) => {
    const navigate = store._navigate;
    const open = (proposal: ProposalView) => navigate({ proposal: proposal.id });

    /** Read the list again and move to the next row, as the claims queue does. */
    const settle = (proposal: ProposalView) => {
      patchState(store, { busy: null });
      if (store.selectedId() === proposal.id) {
        const list = store.proposals();
        const next = list[list.findIndex((candidate) => candidate.id === proposal.id) + 1];
        if (next) open(next);
        else navigate({ proposal: null });
      }
      store._list.reload();
    };

    /** The pane decides a batch of one: reported once the server said what became of it. */
    const decided = (proposal: ProposalView) => (answer: ProposalDecisionsResponse) => {
      store._usage.proposalsDecided(answer, { proposed: 1, unticked: 0 });
      settle(proposal);
    };

    const failed = (proposal: ProposalView) => (failure: unknown) => {
      if (isSessionExpired(failure)) return;
      // A 409 or a refusal: somebody else decided it, or it changed. The list is
      // the server's, so reading it again shows what is true now.
      settle(proposal);
    };

    return {
      filterBy(key: string): void {
        navigate({ filter: key === 'ToReview' ? null : key, page: null, proposal: null });
      },
      open,
      close(): void {
        navigate({ proposal: null });
      },
      approve(proposal: ProposalView): void {
        patchState(store, { busy: proposal.id });
        store._steward.approve([proposal.id]).subscribe({ next: decided(proposal), error: failed(proposal) });
      },
      reject(proposal: ProposalView, reason: string): void {
        patchState(store, { busy: proposal.id });
        store._steward.reject([proposal.id], reason).subscribe({ next: decided(proposal), error: failed(proposal) });
      },
    };
  }),
);
