import { computed, inject } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  patchState,
  signalMethod,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import type { SearchAnswer, SearchFacet, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import {
  catchError,
  debounceTime,
  EMPTY,
  exhaustMap,
  filter,
  finalize,
  firstValueFrom,
  map,
  pipe,
  Subject,
  switchMap,
  takeUntil,
  tap,
  timeout,
} from 'rxjs';
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
import { extendResource } from '@ngrx/signals/resource';

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
  /** The page the rows reach: what the next *Load more* asks for. */
  readonly _page: number;
  /** The question the rows answer: what tells a new one from the same one asked again. */
  readonly _asked: { readonly place: ListingPlace; readonly key: string; readonly culture: string } | null;
}

/** One question: what to list, narrowed how, in which language. */
interface ListingQuestion {
  readonly place: ListingPlace | null;
  readonly filters: ListingFilters;
  readonly culture: string;
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
  withState<ListingStoreState>(() => ({
    place: null,
    result: { status: 'idle' },
    loadingMore: false,
    folded: readFolded(),
    pending: null,
    _page: 1,
    _asked: null,
  })),
  withProps(({ pending }) => {
    const route = inject(ActivatedRoute);
    return {
      _router: inject(Router),
      _route: route,
      _search: inject(ProductSearchService),
      _culture: inject(CultureStore),
      _fromUrl: toSignal(route.queryParamMap.pipe(map(parseListing)), {
        initialValue: parseListing(route.snapshot.queryParamMap),
      }),
      /** Every answer, with the question it answers: what `applyAndSettle` waits on. */
      _answers: new Subject<{ readonly key: string; readonly result: ListingResult }>(),
      /** A new question: the page being appended to the old answer is no longer wanted. */
      _newQuestion: new Subject<void>(),
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
    _preview: extendResource(
      rxResource({
        params: () => {
          const pending = store._settled();
          const place = store.place();
          if (!pending || !place || sameFilters(pending, store.filters())) return undefined;
          return { place, pending, culture: store._culture.active() };
        },
        stream: ({ params }) => store._search.list(searchParams(params.place, params.pending, params.culture, 1, 1)),
      }),
    ),
  })),
  withComputed((store) => {
    /** Whether the preview answers the batch as it stands now, not one from a tap ago. */
    const previewCurrent = computed(() => {
      const pending = store.pending();
      return (
        pending !== null && store._settled() === pending && store._preview.hasValue() && !store._preview.isLoading()
      );
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
    /** A choice made: a navigation, so it is in the history and in the address. */
    const apply = (filters: ListingFilters): void => {
      void store._router.navigate([], {
        relativeTo: store._route,
        queryParams: listingQueryParams(filters),
        queryParamsHandling: 'merge',
      });
    };

    const keyOf = (place: ListingPlace, filters: ListingFilters) =>
      JSON.stringify({ place, filters: listingQueryParams(filters) });

    return {
      /** What the page lists; null lists nothing. A page hands in a signal, and the listing follows it. */
      list: signalMethod<ListingPlace | null>((place) => patchState(store, { place })),
      apply,
      /**
       * A choice made and its answer awaited: the browser agent's `apply_filter`
       * (F-5), which tells the agent what the list now holds. It is `apply`, the
       * same navigation a click makes, so the shopper sees the list change and the
       * back button undoes it; the wait is the only thing added.
       */
      async applyAndSettle(filters: ListingFilters, waitMs = 10_000): Promise<ListingResult> {
        if (sameFilters(filters, store.filters())) return store.result();

        const before = store._asked()?.key;
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
       * `exhaustMap`: a press while a page is still coming is the same press.
       */
      loadMore: rxMethod<void>(
        pipe(
          exhaustMap(() => {
            const current = store.result();
            const place = store.place();
            if (current.status !== 'ready' || !place) return EMPTY;
            const next = store._page() + 1;
            patchState(store, { loadingMore: true });

            return store._search
              .list(searchParams(place, store.filters(), store._culture.active(), next, ListingPageSize))
              .pipe(
                // A new question makes the page this one would append to obsolete.
                takeUntil(store._newQuestion),
                tap((page) =>
                  patchState(store, {
                    _page: next,
                    result: { ...current, hits: [...current.hits, ...page.hits], total: page.total },
                  }),
                ),
                // Nothing is lost and nothing is claimed: the rows already read stay,
                // and the button comes back so it can be tried again.
                catchError(() => EMPTY),
                finalize(() => patchState(store, { loadingMore: false })),
              );
          }),
        ),
      ),
      /**
       * Asks the search whenever the question changes. `switchMap` is the rule the
       * spec holds: a newer question cancels the older one's request, so only the
       * latest answer is ever shown, whatever order the answers arrive in.
       */
      _ask: rxMethod<ListingQuestion>(
        pipe(
          // The same question asked again (a navigation that changed nothing) is not a new one.
          filter(({ place, filters, culture }) => {
            const asked = store._asked();
            return !(place && asked?.key === keyOf(place, filters) && asked.culture === culture);
          }),
          tap(() => {
            store._newQuestion.next();
            patchState(store, { loadingMore: false });
          }),
          switchMap(({ place, filters, culture }) => {
            if (!place) {
              patchState(store, { _asked: null, result: { status: 'idle' } });
              return EMPTY;
            }

            const asked = store._asked();
            const key = keyOf(place, filters);
            const afterCultureChange = asked?.key === key;
            // The same place narrowed differently keeps its rows on screen while the
            // answer comes, and its facets' shape; a new place starts from nothing.
            const samePlace =
              asked !== null &&
              asked.culture === culture &&
              asked.place.query === place.query &&
              asked.place.category === place.category;
            const current = store.result();
            patchState(store, {
              _asked: { place, key, culture },
              _page: 1,
              result: current.status === 'ready' && samePlace ? { ...current, busy: true } : { status: 'loading' },
            });

            const answered = () => store._answers.next({ key, result: store.result() });
            return store._search.list(searchParams(place, filters, culture, 1, ListingPageSize)).pipe(
              tap((page) => {
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
              }),
              // A failure is said in the state; the stream that asked lives on for the next question.
              catchError(() => {
                patchState(store, { result: { status: 'failed' } });
                answered();
                return EMPTY;
              }),
            );
          }),
        ),
      ),
    };
  }),
  withHooks((store) => {
    const active = inject(ActiveListing);
    return {
      onInit(): void {
        active.attach(store);
        // A language switch asks again as well, and the listing says when an answer came from one.
        store._ask(
          computed<ListingQuestion>(() => ({
            place: store.place(),
            filters: store.filters(),
            culture: store._culture.active(),
          })),
        );
      },
      // The requests end with the store's injector: rxMethod unsubscribes them itself.
      onDestroy(): void {
        active.detach(store);
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
