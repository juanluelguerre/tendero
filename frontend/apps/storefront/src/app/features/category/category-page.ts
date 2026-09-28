import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { map, type Subscription } from 'rxjs';
import { CultureStore, PageTitles } from '@tendero/shared-i18n';
import { Telemetry } from '@tendero/shared-telemetry';
import { ShopFrontService, type Category } from '../../data-access/shop-front.service';
import { Listing } from '../../listing/listing';
import { hasFilters } from '../../listing/listing-filters';
import { ListingState } from '../../listing/listing-state';
import { childLinks } from '../../listing/listing-view';
import { ShopLinks } from '../../shop-links';
import { departmentArt, rootOf } from '../../shop-art';
import { openedVia } from './opened-via';

/** The taxonomy's answer about the code in the URL; the products are the listing's. */
type PageState = 'loading' | 'found' | 'unknown' | 'failed';

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
 */
@Component({
  selector: 'storefront-category-page',
  imports: [TranslocoDirective, RouterLink, Listing],
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Its own listing: the filters in the URL, the request, the phone's batch.
  providers: [ListingState],
  templateUrl: './category-page.html',
  styleUrl: './category-page.css',
})
export class CategoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly shopFront = inject(ShopFrontService);
  private readonly culture = inject(CultureStore);
  private readonly titles = inject(PageTitles);
  private readonly router = inject(Router);
  private readonly telemetry = inject(Telemetry);

  /** The listing already reported, so a change of language or of a filter is not a second opening. */
  private reported: string | null = null;

  protected readonly links = inject(ShopLinks);
  protected readonly listing = inject(ListingState);
  protected readonly state = signal<PageState>('loading');

  /** The category itself, for the heading and the breadcrumb. */
  protected readonly category = signal<Category | null>(null);

  /** Its children, so a department offers the sections inside it. */
  protected readonly children = signal<Category[]>([]);

  /** The sections again, as the column's links, with the counts the listing's category facet gives them. */
  protected readonly navigation = computed(() => {
    const category = this.category();
    const result = this.listing.result();
    return category
      ? childLinks(category.code, this.children(), result.status === 'ready' ? result.facets : [])
      : null;
  });

  protected readonly filtered = computed(() => hasFilters(this.listing.filters()));

  /**
   * The department's illustration, borrowed by everything under it.
   *
   * `COFFEE_MAKER` does not get a drawing of its own — it gets Hogar's, because
   * that is the thing a shopper recognises and because art per leaf is
   * twenty-three drawings to keep in step with a taxonomy that changes.
   */
  protected readonly art = signal<string | null>(null);

  private readonly code = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('code') ?? '')),
    { initialValue: '' },
  );

  /**
   * The taxonomy request being answered, so a change of code or language
   * cancels it rather than racing it: without this the answer that arrived
   * LAST won, whichever had been asked for last.
   */
  private inFlight?: Subscription;

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

  constructor() {
    inject(DestroyRef).onDestroy(() => this.inFlight?.unsubscribe());

    effect(() => {
      const code = this.code();
      const culture = this.culture.active();

      if (!code) return;

      this.inFlight?.unsubscribe();
      this.state.set('loading');

      // The taxonomy first, because a code nobody recognises is a 404 rather
      // than an empty grid — "no products here" and "no such category" are
      // different answers and only one of them is the shopper's fault.
      this.inFlight = this.shopFront.categories(culture).subscribe({
        next: (result) => {
          const found = result.items.find(
            (item) => item.code.toLowerCase() === code.toLowerCase(),
          );

          this.category.set(found ?? null);
          // The tab names the section, not "Section".
          if (found) this.titles.name(found.name);
          this.children.set(
            found ? result.items.filter((item) => item.parent === found.code) : [],
          );
          this.art.set(
            found ? departmentArt(rootOf(found.code, result.items)) : null,
          );

          if (!found) {
            this.state.set('unknown');
            this.listing.place.set(null);
            return;
          }

          this.reportOpened(found.code);
          this.state.set('found');
          this.listing.place.set({ query: '', category: found.code });
        },
        error: () => this.state.set('failed'),
      });
    });
  }
}
