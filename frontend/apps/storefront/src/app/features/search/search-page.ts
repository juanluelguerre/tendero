import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { AuthStore } from '@tendero/shared-auth';
import { ReturnsPolicy } from '../../data-access/returns-policy.service';
import { Listing } from '../../listing/listing';
import { hasFilters, listingQueryParams, NoFilters } from '../../listing/listing-filters';
import { ListingState } from '../../listing/listing-state';
import { HomeSections } from '../home/home-sections';
import { ShopLinks } from '../../shop-links';

/** Below this, a query matches so much that the answer is noise. */
const MinimumQueryLength = 2;

/**
 * What the page is showing: the shop's front, a query too short to send, or a
 * listing — which a query OR a filter alone makes, so `/es?offer=1` lists
 * everything on offer (R-7.3, F-3).
 */
type SearchMode = 'idle' | 'tooShort' | 'listing';

/**
 * The storefront's home is the search: the first thing a shopkeeper does is
 * listen to what you are looking for. What is seen here is EXACTLY what the NDCG
 * gate measures (tools/SearchEval) — same endpoint, same ranking.
 */
@Component({
  selector: 'storefront-search-page',
  imports: [TranslocoDirective, RouterLink, HomeSections, Listing],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Its own listing: the filters in the URL, the request, the phone's batch.
  providers: [ListingState],
  templateUrl: './search-page.html',
  styleUrl: './search-page.css',
})
export class SearchPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  /** Whether to offer the way back to an order somebody already placed. */
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  private readonly transloco = inject(TranslocoService);
  private readonly returns = inject(ReturnsPolicy);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /**
   * The results are one listing with the category pages (R-7.3, F-3): the
   * filters, the count, the chips and the order are the listing's, and this
   * page says what it lists — the words in the URL.
   */
  protected readonly listing = inject(ListingState);

  /** The window the shop actually honours, read once and shared with the order
   *  page's own copy of this number (`ReturnsPolicy`). */
  protected readonly returnsWindowDays = this.returns.windowDays;

  /**
   * The query the URL is carrying. It is what makes a result page shareable,
   * bookmarkable and — once there is server rendering — crawlable, and its
   * absence was a row in the standing backlog: the query lived in a signal, so
   * two people could not look at the same results.
   *
   * It is also the half an agent needs. Handing a person a link to what it
   * found is the cheapest thing an agent surface can do, and it cannot do it
   * against a URL that says nothing.
   */
  protected readonly queryFromUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('q') ?? '')),
    { initialValue: '' },
  );

  private readonly query = computed(() => this.queryFromUrl().trim());

  protected readonly filtered = computed(() => hasFilters(this.listing.filters()));

  /**
   * A single character is not a search, and saying so is not pedantry: the
   * alternative is pressing the button and getting nothing back, which reads
   * as a broken shop rather than as a query that was never sent.
   */
  protected readonly mode = computed<SearchMode>(() => {
    const query = this.query();
    if (query.length > 0 && query.length < MinimumQueryLength) return 'tooShort';
    return query.length > 0 || this.filtered() ? 'listing' : 'idle';
  });

  constructor() {
    // The URL is the source of truth for what is listed, so back and forward
    // work without anything extra: the browser changes the parameters and the
    // listing asks again. Typing into the box only navigates. A language switch
    // asks again as well, and the listing says when an answer came from one.
    effect(() => {
      const mode = this.mode();
      this.listing.place.set(mode === 'listing' ? { query: this.query(), category: null } : null);
    });
  }

  /**
   * Real queries from the golden set, not ones chosen because they look good.
   * The first thing a visitor clicks is then a query the NDCG gate actually
   * measures — if one of these ever stops returning something, the gate said so
   * before the shop did.
   *
   * They live in the translation files and not here because a suggestion IS a
   * query: offering "zapatillas running" to somebody reading an English page
   * would send a Spanish query to the English index, which is precisely the
   * mismatch phase 2 spent a whole phase removing.
   */
  protected readonly suggestions = toSignal(
    this.transloco
      .selectTranslateObject<string[]>('search.suggestions')
      // `selectTranslateObject` waits for the language file and re-emits on
      // every change; a computed over `translateObject` does neither, and read
      // during the switch it returned the KEY as a string. `@for` over a string
      // iterates characters, so the chips came out as s · e · a · r · c · h —
      // a failure with no error, no warning and nothing in the console, visible
      // only by looking at the page.
      .pipe(map((value) => (Array.isArray(value) ? value : []))),
    { initialValue: [] as string[] },
  );

  /** Whether the search band should step back and let the results have the room. */
  protected hasResults(): boolean {
    return this.mode() !== 'idle';
  }

  /** The page's heading once it lists something: what was asked, or which shelf the filters make. */
  protected heading(t: (key: string, params?: Record<string, unknown>) => string): string {
    const query = this.query();
    const filters = this.listing.filters();
    if (this.mode() !== 'listing') return t('search.heading');
    if (query) return t('listing.title.query', { query });
    if (filters.onOffer) return t('listing.title.offers');
    if (filters.newArrivals) return t('listing.title.newArrivals');
    return t('listing.title.all');
  }

  /**
   * Pressing search does not search: it navigates. The URL then carries the
   * query, and the listing runs it — one path in, so the answer is the same
   * whether somebody typed it, pressed a suggestion, followed a link or used
   * the back button. A new question starts with no filters: a price range
   * chosen for backpacks says nothing about coffee makers.
   */
  protected submit(query: string): void {
    const text = query.trim();

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { ...listingQueryParams(NoFilters), q: text.length > 0 ? text : null },
      queryParamsHandling: 'merge',
      // A search is not a page in its own history entry: pressing back after
      // three refinements should leave the shop, not walk back through them.
      replaceUrl: true,
    });
  }
}
