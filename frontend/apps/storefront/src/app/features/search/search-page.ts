import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { map } from 'rxjs';
import { CultureStore } from '@tendero/shared-i18n';
import { Campaigns } from '../../campaigns/campaigns';
import { dayOf } from '../../campaigns/campaign-rules';
import { ReturnsPolicy } from '../../data-access/returns-policy.service';
import { Listing } from '../../listing/listing';
import { hasFilters } from '../../listing/listing-filters';
import { ListingState } from '../../listing/listing-state';
import { HomeSections } from '../home/home-sections';
import { ShopLinks } from '../../shop-links';
import { Trail } from '../../trail/trail';

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

  private readonly returns = inject(ReturnsPolicy);
  private readonly route = inject(ActivatedRoute);
  private readonly trail = inject(Trail);
  private readonly campaigns = inject(Campaigns);
  private readonly culture = inject(CultureStore);

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

    // *Sigue donde lo dejaste* (R-8): a search is remembered once it has been
    // listed — words that were answered, not every keystroke, and not a query
    // too short to send. The trail decides where, and whether.
    effect(() => {
      const query = this.query();
      const result = this.listing.result();
      if (this.mode() === 'listing' && query && result.status === 'ready' && !result.busy) {
        untracked(() => this.remember(query));
      }
    });
  }

  private remembered: string | null = null;

  private remember(query: string): void {
    if (this.remembered === query) return;
    this.remembered = query;
    this.trail.recordSearch(query);
  }

  /**
   * The campaign a listing is narrowed to (`?campaign=`, R-8), once the shop's
   * list has answered: its name heads the page, and before it starts the page
   * says when its deals will be here instead of an unexplained empty list.
   */
  protected readonly campaign = computed(() => {
    const code = this.listing.filters().campaign;
    return code ? this.campaigns.named(code) : null;
  });

  protected campaignStarts(): string {
    const campaign = this.campaign();
    return campaign ? dayOf(campaign.startsAt, this.culture.active()) : '';
  }

  /** The page's heading once it lists something: what was asked, or which shelf the filters make. */
  protected heading(t: (key: string, params?: Record<string, unknown>) => string): string {
    const query = this.query();
    const filters = this.listing.filters();
    if (this.mode() !== 'listing') return t('search.heading');
    if (query) return t('listing.title.query', { query });
    if (filters.campaign) return t('listing.title.campaign', { name: this.campaign()?.name ?? filters.campaign });
    if (filters.onOffer) return t('listing.title.offers');
    if (filters.newArrivals) return t('listing.title.newArrivals');
    return t('listing.title.all');
  }
}
