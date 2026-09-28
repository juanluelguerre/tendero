import { computed, DestroyRef, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { rxResource, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import type { SearchAnswer, SearchFacet, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { debounceTime, filter, firstValueFrom, map, Subject, timeout, type Subscription } from 'rxjs';
import { ProductSearchService } from '../data-access/product-search.service';
import { ActiveListing } from './active-listing';
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

/**
 * One listing: search results and a category page are the same list with a
 * different place (R-7.3, F-3), so they share this and the components in
 * `listing/`. Each page provides its own instance and says what it lists by
 * setting `place`; the filters are read from the URL, and every change to them
 * is a navigation, so the back button undoes one step.
 *
 * It is the search endpoint every time. A category page with a second way of
 * listing products would have one the relevance gate measures and one nobody
 * checks.
 */
@Injectable()
export class ListingState {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly search = inject(ProductSearchService);
  private readonly culture = inject(CultureStore);

  /** What is listed; null lists nothing (the home page, a query too short, an unknown category). */
  readonly place = signal<ListingPlace | null>(null);

  private readonly fromUrl = toSignal(this.route.queryParamMap.pipe(map(parseListing)), {
    initialValue: parseListing(this.route.snapshot.queryParamMap),
  });

  /** The filters the URL holds. On a category page `within` means nothing: the category IS the place. */
  readonly filters = computed<ListingFilters>(() => {
    const filters = this.fromUrl();
    return this.place()?.category && filters.within.length ? { ...filters, within: [] } : filters;
  });

  readonly applied = computed(() => appliedCount(this.filters()));

  readonly result = signal<ListingResult>({ status: 'idle' });
  readonly loadingMore = signal(false);

  /** The column folded away on a desktop, remembered per browser (the spec: a convenience, so `localStorage`). */
  readonly folded = signal(readFolded());

  private page = 1;
  private asked: { place: ListingPlace; key: string; culture: string } | null = null;

  /** Every answer, with the question it answers: what `settle` waits on. */
  private readonly answers = new Subject<{ readonly key: string; readonly result: ListingResult }>();
  private inFlight?: Subscription;
  private appending?: Subscription;

  constructor() {
    const active = inject(ActiveListing);
    active.attach(this);

    inject(DestroyRef).onDestroy(() => {
      active.detach(this);
      this.inFlight?.unsubscribe();
      this.appending?.unsubscribe();
    });

    effect(() => {
      const place = this.place();
      const filters = this.filters();
      const culture = this.culture.active();
      untracked(() => this.fetch(place, filters, culture));
    });
  }

  /** A choice made: a navigation, so it is in the history and in the address. */
  apply(filters: ListingFilters): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: listingQueryParams(filters),
      queryParamsHandling: 'merge',
    });
  }

  /**
   * A choice made and its answer awaited: the browser agent's `apply_filter`
   * (F-5), which tells the agent what the list now holds. It is `apply`, the
   * same navigation a click makes, so the shopper sees the list change and the
   * back button undoes it; the wait is the only thing added.
   */
  async applyAndSettle(filters: ListingFilters, waitMs = 10_000): Promise<ListingResult> {
    if (sameFilters(filters, this.filters())) return this.result();

    const before = this.asked?.key;
    // Listening before navigating: the answer cannot come before the question,
    // and an answer to the question already in flight is not this one.
    const answer = firstValueFrom(
      this.answers.pipe(
        filter((candidate) => candidate.key !== before),
        map((candidate) => candidate.result),
        timeout(waitMs),
      ),
    );

    this.apply(filters);
    return answer;
  }

  toggleFolded(): void {
    const folded = !this.folded();
    this.folded.set(folded);
    try {
      localStorage.setItem(FoldedKey, folded ? '1' : '0');
    } catch {
      // Private windows and blocked storage: the column folds for this visit only.
    }
  }

  /**
   * The next page, appended. A department of fifty-five products that shows
   * twenty-four and stops is a shop with thirty-one things you cannot reach.
   */
  loadMore(): void {
    const current = this.result();
    const place = this.place();
    if (current.status !== 'ready' || !place || this.loadingMore()) return;

    const next = this.page + 1;
    this.loadingMore.set(true);

    this.appending = this.search
      .list(searchParams(place, this.filters(), this.culture.active(), next, ListingPageSize))
      .subscribe({
        next: (page) => {
          this.page = next;
          this.loadingMore.set(false);
          this.result.set({ ...current, hits: [...current.hits, ...page.hits], total: page.total });
        },
        // Nothing is lost and nothing is claimed: the rows already read stay,
        // and the button comes back so it can be tried again.
        error: () => this.loadingMore.set(false),
      });
  }

  // ---------- The phone's batch ----------

  /**
   * The choices made in the phone's sheet and not yet applied; null while it is
   * closed. The page does not reload under a thumb on every tap: the batch is
   * applied with *Ver N productos*.
   */
  readonly pending = signal<ListingFilters | null>(null);

  private readonly settled = toSignal(toObservable(this.pending).pipe(debounceTime(PreviewDelayMs)), {
    initialValue: null,
  });

  /** What the batch would leave, asked once the taps settle: one product is enough to learn the count and the facets. */
  private readonly preview = rxResource({
    params: () => {
      const pending = this.settled();
      const place = this.place();
      if (!pending || !place || sameFilters(pending, this.filters())) return undefined;
      return { place, pending, culture: this.culture.active() };
    },
    stream: ({ params }) => this.search.list(searchParams(params.place, params.pending, params.culture, 1, 1)),
  });

  /** Whether the preview answers the batch as it stands now, not one from a tap ago. */
  private readonly previewCurrent = computed(() => {
    const pending = this.pending();
    return pending !== null && this.settled() === pending && this.preview.hasValue() && !this.preview.isLoading();
  });

  /** *Ver N productos*: N, or null while it is not known. */
  readonly pendingTotal = computed<number | null>(() => {
    const pending = this.pending();
    const current = this.result();
    if (!pending) return null;
    if (sameFilters(pending, this.filters())) return current.status === 'ready' ? current.total : null;
    return this.previewCurrent() ? (this.preview.value()?.total ?? null) : null;
  });

  /** The sheet's facets: the batch's own counts once known, the list's until then, the shape kept either way. */
  readonly pendingFacets = computed<readonly SearchFacet[]>(() => {
    const current = this.result();
    const facets = current.status === 'ready' ? current.facets : [];
    if (!this.previewCurrent()) return facets;
    return keepShape(facets, this.preview.value()?.facets ?? []);
  });

  // ---------- Asking ----------

  private fetch(place: ListingPlace | null, filters: ListingFilters, culture: string): void {
    this.appending?.unsubscribe();
    this.loadingMore.set(false);

    if (!place) {
      this.inFlight?.unsubscribe();
      this.asked = null;
      this.result.set({ status: 'idle' });
      return;
    }

    const key = JSON.stringify({ place, filters: listingQueryParams(filters) });
    const previous = this.asked;
    if (previous?.key === key && previous.culture === culture) return;

    const afterCultureChange = previous?.key === key;
    // The same place narrowed differently keeps its rows on screen while the
    // answer comes, and its facets' shape; a new place starts from nothing.
    const samePlace = previous !== null && previous.culture === culture
      && previous.place.query === place.query && previous.place.category === place.category;
    this.asked = { place, key, culture };
    this.page = 1;

    const current = this.result();
    this.result.set(current.status === 'ready' && samePlace ? { ...current, busy: true } : { status: 'loading' });

    this.inFlight?.unsubscribe();
    this.inFlight = this.search.list(searchParams(place, filters, culture, 1, ListingPageSize)).subscribe({
      next: (page) => {
        const before = this.result();
        const facets = page.facets ?? [];
        this.result.set({
          status: 'ready',
          hits: page.hits,
          total: page.total,
          facets: before.status === 'ready' && samePlace ? keepShape(before.facets, facets) : facets,
          tookMs: page.tookMs,
          // The schema marks it optional as well as nullable: a browse answers with neither.
          answer: page.answer ?? null,
          afterCultureChange,
          busy: false,
        });
        this.answers.next({ key, result: this.result() });
      },
      error: () => {
        this.result.set({ status: 'failed' });
        this.answers.next({ key, result: this.result() });
      },
    });
  }
}

function readFolded(): boolean {
  try {
    return localStorage.getItem(FoldedKey) === '1';
  } catch {
    return false;
  }
}
