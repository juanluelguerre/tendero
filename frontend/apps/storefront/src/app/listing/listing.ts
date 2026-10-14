import {
  Component,
  computed,
  DestroyRef,
  effect,
  type ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import { Telemetry } from '@tendero/shared-telemetry';
import { CultureStore, DEFAULT_CULTURE, SUPPORTED_CULTURES } from '@tendero/shared-i18n';
import { Icon } from '@tendero/shared-ui';
import { CampaignsStore } from '../campaigns/campaigns.store';
import { ProductCard } from '../product-card/product-card';
import { CanonicalLinks } from '../seo/canonical-links';
import { RobotsMeta } from '../seo/robots-meta';
import { ShopLinks } from '../shop-links';
import { FilterPanel } from './filter-panel/filter-panel';
import {
  clearAll,
  hasFilters,
  ListingSorts,
  narrowedFacets,
  withSort,
  type ListingFilters,
  type ListingSort,
} from './listing-filters';
import { listingIndexing } from './listing-indexing';
import { ListingPageSize, ListingStore } from './listing.store';
import { chipsOf, sayablePrice, type CategoryLink, type Chip } from './listing-view';
import { ShopCurrency } from '@tendero/shared-util';

/** The template's `t`, handed to the methods that word things, so a language switch rewords them. */
type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * The listing: search results and a category page draw this (R-7.3, F-3 and
 * F-4). A toolbar with the count, the applied chips and the order; the filters
 * in a column beside the results on a desktop, folding away, and in a modal
 * sheet on a phone; the grid and *Mostrar más*.
 *
 * On a desktop each choice applies at once. On a phone the sheet collects a
 * batch and applies it with *Ver N productos*, so the page does not reload
 * under a thumb on every tap (the spec, decided by the author).
 *
 * The page it sits in owns what is above it (the search box, a category's
 * heading and breadcrumb) and may project what goes above the grid — the
 * search's help card and its "nothing matches exactly" heading.
 */
@Component({
  selector: 'storefront-listing',
  imports: [TranslocoDirective, NgTemplateOutlet, ProductCard, FilterPanel, Icon],
  templateUrl: './listing.html',
  // The host's own room above the listing, which its template cannot give itself.
  host: { class: 'block mt-4' },
})
export class Listing {
  /** A category page's sections, drawn as links in the column; null on a search. */
  readonly navigation = input<readonly CategoryLink[] | null>(null);

  protected readonly state = inject(ListingStore);
  private readonly culture = inject(CultureStore);
  private readonly telemetry = inject(Telemetry);
  private readonly campaigns = inject(CampaignsStore);

  private readonly sheet = viewChild<ElementRef<HTMLDialogElement>>('sheet');
  private readonly sheetButton = viewChild<ElementRef<HTMLButtonElement>>('sheetButton');

  /** Whether the sheet has ever been opened, so its panel is drawn only once it is wanted. */
  protected readonly sheetOpened = signal(false);

  protected readonly sorts = ListingSorts;
  protected readonly placeholders = [0, 1, 2, 3];

  protected readonly ready = computed(() => {
    const result = this.state.result();
    return result.status === 'ready' ? result : null;
  });

  protected readonly facets = computed(() => this.ready()?.facets ?? []);

  /** The shop sells in one currency today; the listing reads it from what it lists rather than assuming it. */
  protected readonly currency = computed(() => this.ready()?.hits[0]?.price.currency ?? ShopCurrency);

  /** A search orders by relevance; a listing with no words has nothing to be relevant to, so it is "recommended". */
  protected readonly searching = computed(() => (this.state.place()?.query ?? '') !== '');

  protected readonly filtered = computed(() => hasFilters(this.state.filters()));

  constructor() {
    const canonical = inject(CanonicalLinks);
    const robots = inject(RobotsMeta);
    const links = inject(ShopLinks);

    // What the listing tells a search engine follows what it lists: the rule
    // is `listingIndexing`'s, this only writes it into the head.
    effect(() => {
      const place = this.state.place();
      const indexing = place ? listingIndexing(place, this.state.filters()) : { kind: 'silent' as const };

      canonical.clear();
      robots.clear();

      if (indexing.kind === 'noindex') robots.noindex();
      if (indexing.kind === 'canonical') {
        canonical.set(
          links.categoryIn(this.culture.active(), indexing.category),
          SUPPORTED_CULTURES.map((culture) => ({ culture, path: links.categoryIn(culture, indexing.category) })),
          links.categoryIn(DEFAULT_CULTURE, indexing.category),
        );
      }
    });

    // Leaving the listing takes its tags with it, so a product page is never
    // `noindex` because a search led to it.
    inject(DestroyRef).onDestroy(() => {
      canonical.clear();
      robots.clear();
    });
  }

  // ---------- Wording ----------

  protected chips(t: Translate): Chip[] {
    const money = (amount: number) => sayablePrice(amount, this.currency(), this.culture.active());
    return chipsOf(this.state.filters(), this.facets(), {
      onOffer: t('listing.offer'),
      newArrivals: t('listing.newArrivals'),
      inStock: t('listing.inStock'),
      money,
      campaign: (code) => t('listing.campaign', { name: this.campaigns.named(code)?.name ?? code }),
      upTo: (max) => t('listing.price.upTo', { price: max }),
      from: (min) => t('listing.price.from', { price: min }),
      between: (min, max) => t('listing.price.between', { min, max }),
    });
  }

  /**
   * "1–24 de 55 resultados para «mochila»", or "8 productos" when all of them are on
   * screen. When nothing matched the words, the search answers with the nearest
   * (`Related`) and the count says so — "35 productos parecidos a «…»" — rather
   * than calling them results under a notice that says nothing matched.
   */
  protected count(t: Translate): string {
    const ready = this.ready();
    if (!ready) return t('listing.loading');

    const query = this.state.place()?.query ?? '';
    const prefix = !query
      ? 'listing.count.'
      : ready.answer?.match === 'Related'
        ? 'listing.count.related.'
        : 'listing.count.for.';
    const shown = ready.hits.length;

    if (ready.total === 1) return t(prefix + 'one', { query });
    if (shown > 0 && shown < ready.total) return t(prefix + 'page', { from: 1, to: shown, total: ready.total, query });
    return t(prefix + 'all', { total: ready.total, query });
  }

  /** *Mostrar 14 más*: how many the next page brings, never more than are left. */
  protected nextCount(): number {
    const ready = this.ready();
    return ready ? Math.min(ListingPageSize, ready.total - ready.hits.length) : 0;
  }

  protected hasMore(): boolean {
    const ready = this.ready();
    return ready !== null && ready.hits.length < ready.total;
  }

  // ---------- Choosing ----------

  protected sortBy(value: string): void {
    const sort = (ListingSorts as readonly string[]).includes(value) ? (value as ListingSort) : null;
    this.state.apply(withSort(this.state.filters(), sort));
  }

  /** A choice in the column applies at once. */
  protected applyFromColumn(filters: ListingFilters): void {
    this.report(filters, 'column');
    this.state.apply(filters);
  }

  /** *Quitar todo* beside the chips, or the one a list that filters emptied offers. */
  protected clearAll(surface: 'toolbar' | 'empty'): void {
    this.telemetry.trackEvent('filters_cleared', { surface });
    this.state.apply(clearAll(this.state.filters()));
  }

  /**
   * `filter_applied` once per facet the change narrowed, when it takes effect
   * (F-6, the tracking plan). The facade drops it unless the shopper allowed
   * analytics.
   */
  private report(next: ListingFilters, surface: 'column' | 'sheet'): void {
    const place = this.state.place();
    const listing = place?.category ? 'category' : place?.query ? 'search' : 'listing';
    for (const facet of narrowedFacets(this.state.filters(), next)) {
      this.telemetry.trackEvent('filter_applied', { facet, listing, surface });
    }
  }

  // ---------- The phone's sheet ----------

  /** A modal dialog, like the menu drawer: the platform traps the focus, Esc closes it and the page behind is inert. */
  protected openSheet(): void {
    this.state.batch(this.state.filters());
    this.sheetOpened.set(true);
    this.sheet()?.nativeElement.showModal();
  }

  protected choose(filters: ListingFilters): void {
    this.state.batch(filters);
  }

  protected clearPending(): void {
    const pending = this.state.pending();
    if (pending) this.state.batch(clearAll(pending));
  }

  /** *Ver N productos*: the batch becomes one navigation, one step for the back button. */
  protected applyPending(): void {
    const pending = this.state.pending();
    if (pending) {
      if (hasFilters(this.state.filters()) && !hasFilters(pending)) {
        this.telemetry.trackEvent('filters_cleared', { surface: 'sheet' });
      }
      this.report(pending, 'sheet');
      this.state.apply(pending);
    }
    this.sheet()?.nativeElement.close();
  }

  /** Closed by Esc, the cross or applying: an unapplied batch is dropped, and the focus goes back where it came from. */
  protected sheetClosed(): void {
    this.state.batch(null);
    this.sheetButton()?.nativeElement.focus();
  }
}
