import { computed, effect, inject, untracked } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { patchState, signalStore, withComputed, withHooks, withMethods, withProps, withState } from '@ngrx/signals';
import type { SearchAnswer, SearchFacet, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { debounceTime, filter, firstValueFrom, map, Subject, timeout, type Subscription } from 'rxjs';
import { ProductSearchService } from '../data-access/product-search.service';
import { ActiveListing } from './active-listing.store';
import {
  appliedCount,
  listingQueryParams,
  parseListing,
  sameFilters,
  searchParams,
  type ListingFilters,
  type ListingPlace,
} from './listing-filters';
import { keepShape } from './listing-view';

/** Enough to fill the grid without asking for the whole department. */
export const ListingPageSize = 24;

/** How long the phone's sheet waits after a tap before asking what the batch would leave. */
const PreviewDelayMs = 250;

/** Where the folded column is remembered: a convenience of this browser, nothing more. */
const FoldedKey = 'tendero.listing.filtersFolded';

export type ListingResult =
  | { readonly status: 'idle' }
  | { readonly status: 'loading' }
  | { readonly status: 'failed' }
  | {
      readonly status: 'ready';
      readonly hits: readonly SearchHit[];
      readonly total: number;
      readonly facets: readonly SearchFacet[];
      readonly tookMs: number;
      /** ADR 0032's layer A; null for a browse. */
      readonly answer: SearchAnswer | null;
      /**
       * Whether this answer came from a language switch rather than from a
       * question. It changes what an empty result means: not "we do not sell
       * that" but "that query is in the other language".
       */
      readonly afterCultureChange: boolean;
      /** A newer question is in flight: the rows stay, so nothing jumps, and say they are stale. */
      readonly busy: boolean;
    };

interface ListingStoreState {
  /** What is listed; null lists nothing (the home page, a query too short, an unknown category). */
  readonly place: ListingPlace | null;
  readonly result: ListingResult;
  readonly loadingMore: boolean;
  /** The column folded away on a desktop, remembered per browser (the spec: a convenience, so `localStorage`). */
  readonly folded: boolean;
  /**
   * The choices made in the phone's sheet and not yet applied; null while it is
   * closed. The page does not reload under a thumb on every tap: the batch is
   * applied with *Ver N productos*.
   */
  readonly pending: ListingFilters | null;
}

/**
 * One listing: search results and a category page are the same list with a
 * different place (R-7.3, F-3), so they share this and the components in
 * `listing/`. Each page provides its own store (ADR 0047) and says what it
 * lists with `list(place)`; the filters are read from the URL, and every change
 * to them is a navigation, so the back button undoes one step.
 *
 * It is the search endpoint every time. A category page with a second way of
 * listing products would have one the relevance gate measures and one nobody
 * checks.
 */
export const ListingStore = signalStore(
  withState<ListingStoreState>(() => ({ place: null, result: { status: 'idle' }, loadingMore: false, folded: readFolded(), pending: null })),
  withProps(({ pending }) => {
    const route = inject(ActivatedRoute);
    return {
      _router: inject(Router),
      _route: route,
      _search: inject(ProductSearchService),
      _culture: inject(CultureStore),
      _fromUrl: toSignal(route.queryParamMap.pipe(map(parseListing)), { initialValue: parseListing(route.snapshot.queryParamMap) }),
      /** Every answer, with the question it answers: what `applyAndSettle` waits on. */
      _answers: new Subject<{ readonly key: string; readonly result: ListingResult }>(),
      /** The question being answered and the requests in flight: bookkeeping, not state anybody draws. */
      _asking: {
        page: 1,
        asked: null as { place: ListingPlace; key: string; culture: string } | null,
        inFlight: undefined as Subscription | undefined,
        appending: undefined as Subscription | undefined,
      },
      /** The batch once the taps settle. */
      _settled: toSignal(toObservable(pending).pipe(debounceTime(PreviewDelayMs)), { initialValue: null }),
    };
  }),
  withComputed(({ place, _fromUrl }) => {
    /** The filters the URL holds. On a category page `within` means nothing: the category IS the place. */
    const filters = computed<ListingFilters>(() => {
      const fromUrl = _fromUrl();
      return place()?.category && fromUrl.within.length ? { ...fromUrl, within: [] } : fromUrl;
    });
    return { filters, applied: computed(() => appliedCount(filters())) };
  }),
  withProps((store) => ({
    /** What the batch would leave, asked once the taps settle: one product is enough to learn the count and the facets. */
    _preview: rxResource({
      params: () => {
        const pending = store._settled();
        const place = store.place();
        if (!pending || !place || sameFilters(pending, store.filters())) return undefined;
        return { place, pending, culture: store._culture.active() };
      },
      stream: ({ params }) => store._search.list(searchParams(params.place, params.pending, params.culture, 1, 1)),
    }),
  })),
  withComputed((store) => {
    /** Whether the preview answers the batch as it stands now, not one from a tap ago. */
    const previewCurrent = computed(() => {
      const pending = store.pending();
      return pending !== null && store._settled() === pending && store._preview.hasValue() && !store._preview.isLoading();
    });
    return {
      /** *Ver N productos*: N, or null while it is not known. */
      pendingTotal: computed<number | null>(() => {
        const pending = store.pending();
        const current = store.result();
        if (!pending) return null;
        if (sameFilters(pending, store.filters())) return current.status === 'ready' ? current.total : null;
        return previewCurrent() ? (store._preview.value()?.total ?? null) : null;
      }),
      /** The sheet's facets: the batch's own counts once known, the list's until then, the shape kept either way. */
      pendingFacets: computed<readonly SearchFacet[]>(() => {
        const current = store.result();
        const facets = current.status === 'ready' ? current.facets : [];
        if (!previewCurrent()) return facets;
        return keepShape(facets, store._preview.value()?.facets ?? []);
      }),
    };
  }),
  withMethods((store) => {
    const asking = store._asking;

    /** A choice made: a navigation, so it is in the history and in the address. */
    const apply = (filters: ListingFilters): void => {
      void store._router.navigate([], { relativeTo: store._route, queryParams: listingQueryParams(filters), queryParamsHandling: 'merge' });
    };

    return {
      /** What the page lists; null lists nothing. */
      list(place: ListingPlace | null): void {
        patchState(store, { place });
      },
      apply,
      /**
       * A choice made and its answer awaited: the browser agent's `apply_filter`
       * (F-5), which tells the agent what the list now holds. It is `apply`, the
       * same navigation a click makes, so the shopper sees the list change and the
       * back button undoes it; the wait is the only thing added.
       */
      async applyAndSettle(filters: ListingFilters, waitMs = 10_000): Promise<ListingResult> {
        if (sameFilters(filters, store.filters())) return store.result();

        const before = asking.asked?.key;
        // Listening before navigating: the answer cannot come before the question,
        // and an answer to the question already in flight is not this one.
        const answer = firstValueFrom(
          store._answers.pipe(
            filter((candidate) => candidate.key !== before),
            map((candidate) => candidate.result),
            timeout(waitMs),
          ),
        );

        apply(filters);
        return answer;
      },
      toggleFolded(): void {
        const folded = !store.folded();
        patchState(store, { folded });
        try {
          localStorage.setItem(FoldedKey, folded ? '1' : '0');
        } catch {
          // Private windows and blocked storage: the column folds for this visit only.
        }
      },
      /** The phone's sheet: the batch it holds, or null once it closes. */
      batch(pending: ListingFilters | null): void {
        patchState(store, { pending });
      },
      /**
       * The next page, appended. A department of fifty-five products that shows
       * twenty-four and stops is a shop with thirty-one things you cannot reach.
       */
      loadMore(): void {
        const current = store.result();
        const place = store.place();
        if (current.status !== 'ready' || !place || store.loadingMore()) return;

        const next = asking.page + 1;
        patchState(store, { loadingMore: true });

        asking.appending = store._search
          .list(searchParams(place, store.filters(), store._culture.active(), next, ListingPageSize))
          .subscribe({
            next: (page) => {
              asking.page = next;
              patchState(store, { loadingMore: false, result: { ...current, hits: [...current.hits, ...page.hits], total: page.total } });
            },
            // Nothing is lost and nothing is claimed: the rows already read stay,
            // and the button comes back so it can be tried again.
            error: () => patchState(store, { loadingMore: false }),
          });
      },
      _fetch(place: ListingPlace | null, filters: ListingFilters, culture: string): void {
        asking.appending?.unsubscribe();
        patchState(store, { loadingMore: false });

        if (!place) {
          asking.inFlight?.unsubscribe();
          asking.asked = null;
          patchState(store, { result: { status: 'idle' } });
          return;
        }

        const key = JSON.stringify({ place, filters: listingQueryParams(filters) });
        const previous = asking.asked;
        if (previous?.key === key && previous.culture === culture) return;

        const afterCultureChange = previous?.key === key;
        // The same place narrowed differently keeps its rows on screen while the
        // answer comes, and its facets' shape; a new place starts from nothing.
        const samePlace = previous !== null && previous.culture === culture
          && previous.place.query === place.query && previous.place.category === place.category;
        asking.asked = { place, key, culture };
        asking.page = 1;

        const current = store.result();
        patchState(store, { result: current.status === 'ready' && samePlace ? { ...current, busy: true } : { status: 'loading' } });

        const answered = () => store._answers.next({ key, result: store.result() });
        asking.inFlight?.unsubscribe();
        asking.inFlight = store._search.list(searchParams(place, filters, culture, 1, ListingPageSize)).subscribe({
          next: (page) => {
            const before = store.result();
            const facets = page.facets ?? [];
            patchState(store, {
              result: {
                status: 'ready',
                hits: page.hits,
                total: page.total,
                facets: before.status === 'ready' && samePlace ? keepShape(before.facets, facets) : facets,
                tookMs: page.tookMs,
                // The schema marks it optional as well as nullable: a browse answers with neither.
                answer: page.answer ?? null,
                afterCultureChange,
                busy: false,
              },
            });
            answered();
          },
          error: () => {
            patchState(store, { result: { status: 'failed' } });
            answered();
          },
        });
      },
    };
  }),
  withHooks((store) => {
    const active = inject(ActiveListing);
    return {
      onInit(): void {
        active.attach(store);
        effect(() => {
          const place = store.place();
          const filters = store.filters();
          const culture = store._culture.active();
          untracked(() => store._fetch(place, filters, culture));
        });
      },
      onDestroy(): void {
        active.detach(store);
        store._asking.inFlight?.unsubscribe();
        store._asking.appending?.unsubscribe();
      },
    };
  }),
);

function readFolded(): boolean {
  try {
    return localStorage.getItem(FoldedKey) === '1';
  } catch {
    return false;
  }
}
