import { ChangeDetectionStrategy, Component, computed, effect, inject, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { PageTitles } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { CampaignCard } from '../../campaigns/campaign-card/campaign-card';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { Listing } from '../../listing/listing';
import { hasFilters } from '../../listing/listing-filters';
import { ListingStore } from '../../listing/listing.store';
import { ShopLinks } from '../../shop-links';
import { CategoryStore } from './category.store';
import { openedVia } from './opened-via';
import { Notice } from '@tendero/shared-ui';
import { PageState } from '../../page-state/page-state';
import { Crumbs } from '../../crumbs/crumbs';

/**
 * Everything in one branch of the taxonomy.
 *
 * **It is the search endpoint with no words in it**, and that is the point
 * rather than a shortcut: a shop with a second way of listing products has one
 * that the relevance gate measures and one that nobody checks. The filter is on
 * the branch, so "Hogar" answers with the fifty-five things under it and not
 * with the nothing filed directly against it.
 *
 * The code is in the URL and the name is not, for the reason ADR 0026 gives
 * about products: a name is not stable, and this one exists in two languages.
 *
 * **It is the same listing as the search results** (R-7.3, F-3): a category
 * page is that list with a category chosen, so the filters, the chips, the
 * order and *Mostrar más* are the listing's, and the filters live in the query
 * string beside the code in the path.
 *
 * The branch is its store's (ADR 0047), the products the listing's; the page
 * presents both and names the tab.
 */
@Component({
  selector: 'storefront-category-page',
  imports: [TranslocoDirective, RouterLink, Listing, CampaignCard, Notice, PageState, Crumbs],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Its own listing — the filters in the URL, the request, the phone's batch —
  // and its own branch, which tells the listing what to list.
  providers: [ListingStore, CategoryStore],
  templateUrl: './category-page.html',
})
export class CategoryPage {
  protected readonly store = inject(CategoryStore);
  protected readonly listing = inject(ListingStore);
  protected readonly campaigns = inject(CampaignsStore);
  protected readonly links = inject(ShopLinks);

  private readonly titles = inject(PageTitles);
  private readonly router = inject(Router);
  private readonly telemetry = inject(Telemetry);

  /** The listing already reported, so a change of language or of a filter is not a second opening. */
  private reported: string | null = null;

  protected readonly filtered = computed(() => hasFilters(this.listing.filters()));

  constructor() {
    effect(() => {
      const found = this.store.category();
      if (this.store.state() !== 'found' || !found) return;
      untracked(() => {
        // The tab names the section, not "Section".
        this.titles.name(found.name);
        this.reportOpened(found.code);
      });
    });
  }

  /**
   * `category_opened` (the tracking plan, R-0 of the redesign): once per listing,
   * with how the shopper got there. The facade drops it unless they allowed
   * analytics.
   */
  private reportOpened(code: string): void {
    if (this.reported === code) return;
    this.reported = code;
    this.telemetry.trackEvent('category_opened', { arrived_from: openedVia(this.router.lastSuccessfulNavigation()) });
  }
}
