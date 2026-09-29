import { computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { signalStore, withComputed, withHooks, withProps } from '@ngrx/signals';
import { map } from 'rxjs';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { hasFilters } from '../../listing/listing-filters';
import { ListingStore } from '../../listing/listing.store';
import { TrailStore } from '../../trail/trail.store';

/** Below this, a query matches so much that the answer is noise. */
const MinimumQueryLength = 2;

/**
 * What the page is showing: the shop's front, a query too short to send, or a
 * listing — which a query OR a filter alone makes, so `/es?offer=1` lists
 * everything on offer (R-7.3, F-3).
 */
export type SearchMode = 'idle' | 'tooShort' | 'listing';

/**
 * The search page's own state (ADR 0047): the words the URL carries, what they
 * make of the page, and what the page's listing lists. The listing's store is
 * provided by the same page; this tells it what.
 *
 * The URL is the source of truth for what is listed, so back and forward work
 * without anything extra: the browser changes the parameters and the listing
 * asks again. Typing into the header's box only navigates.
 */
export const SearchStore = signalStore(
  withProps(() => {
    const route = inject(ActivatedRoute);
    return {
      _listing: inject(ListingStore),
      _trail: inject(TrailStore),
      _campaigns: inject(CampaignsStore),
      /**
       * The query the URL is carrying. It is what makes a result page
       * shareable, bookmarkable and — once there is server rendering —
       * crawlable, and its absence was a row in the standing backlog: the query
       * lived in a signal, so two people could not look at the same results.
       *
       * It is also the half an agent needs. Handing a person a link to what it
       * found is the cheapest thing an agent surface can do, and it cannot do
       * it against a URL that says nothing.
       */
      queryFromUrl: toSignal(route.queryParamMap.pipe(map((params) => params.get('q') ?? '')), { initialValue: '' }),
      /** The search already remembered, so a new answer to the same words is not a second one. */
      _remembered: { query: null as string | null },
    };
  }),
  withComputed(({ queryFromUrl, _listing, _campaigns }) => {
    const query = computed(() => queryFromUrl().trim());
    const filtered = computed(() => hasFilters(_listing.filters()));
    return {
      query,
      filtered,
      /**
       * A single character is not a search, and saying so is not pedantry: the
       * alternative is pressing the button and getting nothing back, which
       * reads as a broken shop rather than as a query that was never sent.
       */
      mode: computed<SearchMode>(() => {
        const words = query();
        if (words.length > 0 && words.length < MinimumQueryLength) return 'tooShort';
        return words.length > 0 || filtered() ? 'listing' : 'idle';
      }),
      /**
       * The campaign a listing is narrowed to (`?campaign=`, R-8), once the
       * shop's list has answered: its name heads the page, and before it starts
       * the page says when its deals will be here instead of an unexplained
       * empty list.
       */
      campaign: computed(() => {
        const code = _listing.filters().campaign;
        return code ? _campaigns.named(code) : null;
      }),
    };
  }),
  withHooks({
    onInit(store) {
      // A language switch asks again as well, and the listing says when an
      // answer came from one.
      effect(() => {
        const listing = store.mode() === 'listing';
        const query = store.query();
        untracked(() => store._listing.list(listing ? { query, category: null } : null));
      });

      // *Sigue donde lo dejaste* (R-8): a search is remembered once it has been
      // listed — words that were answered, not every keystroke, and not a query
      // too short to send. The trail decides where, and whether.
      effect(() => {
        const query = store.query();
        const result = store._listing.result();
        if (store.mode() !== 'listing' || !query || result.status !== 'ready' || result.busy) return;
        if (store._remembered.query === query) return;
        store._remembered.query = query;
        untracked(() => store._trail.recordSearch(query));
      });
    },
  }),
);
