import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { dayOf } from '../../campaigns/campaign-rules';
import { Listing } from '../../listing/listing';
import { ListingStore } from '../../listing/listing.store';
import { ReturnsPolicyStore } from '../../returns/returns-policy.store';
import { ShopLinks } from '../../shop-links';
import { HomeSections } from '../home/home-sections';
import { SearchStore } from './search.store';
import { Notice } from '@tendero/shared-ui';
import { PageState } from '../../page-state/page-state';

/**
 * The storefront's home is the search: the first thing a shopkeeper does is
 * listen to what you are looking for. What is seen here is EXACTLY what the NDCG
 * gate measures (tools/SearchEval) — same endpoint, same ranking.
 *
 * What the page shows is its store's and its listing's (ADR 0047); the page
 * words it: the heading, the day a campaign starts.
 */
@Component({
  selector: 'storefront-search-page',
  imports: [TranslocoDirective, RouterLink, HomeSections, Listing, Notice, PageState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Its own listing — the filters in the URL, the request, the phone's batch —
  // and its own words, which tell the listing what to list.
  providers: [ListingStore, SearchStore],
  templateUrl: './search-page.html',
  styleUrl: './search-page.css',
})
export class SearchPage {
  protected readonly store = inject(SearchStore);

  /**
   * The results are one listing with the category pages (R-7.3, F-3): the
   * filters, the count, the chips and the order are the listing's, and this
   * page says what it lists — the words in the URL.
   */
  protected readonly listing = inject(ListingStore);

  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  private readonly culture = inject(CultureStore);

  /** The window the shop actually honours, read once and shared with the order
   *  page's own copy of this number (`ReturnsPolicyStore`). */
  protected readonly returnsWindowDays = inject(ReturnsPolicyStore).windowDays;

  protected campaignStarts(): string {
    const campaign = this.store.campaign();
    return campaign ? dayOf(campaign.startsAt, this.culture.active()) : '';
  }

  /** The page's heading once it lists something: what was asked, or which shelf the filters make. */
  protected heading(t: (key: string, params?: Record<string, unknown>) => string): string {
    const query = this.store.query();
    const filters = this.listing.filters();
    if (this.store.mode() !== 'listing') return t('search.heading');
    if (query) return t('listing.title.query', { query });
    if (filters.campaign) return t('listing.title.campaign', { name: this.store.campaign()?.name ?? filters.campaign });
    if (filters.onOffer) return t('listing.title.offers');
    if (filters.newArrivals) return t('listing.title.newArrivals');
    return t('listing.title.all');
  }
}
