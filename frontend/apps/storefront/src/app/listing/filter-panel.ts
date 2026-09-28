import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { FacetKind, FacetOption, PriceBand, SearchFacet } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Taxonomy } from '../data-access/taxonomy';
import { ShopLinks } from '../shop-links';
import { isChosen, parseAmount, toggleOption, withPrice, type ListingFilters } from './listing-filters';
import {
  brandsToShow,
  categoryLevel,
  isDead,
  priceLabel,
  safeSwatch,
  sayablePrice,
  sectionsOf,
  type CategoryLink,
} from './listing-view';

/** Each panel's ids are its own: the column and the phone's sheet can both be in the page. */
let panels = 0;

/**
 * The filters, section by section (R-7.3, F-3 and F-4): the desktop's column
 * and the phone's sheet draw the same panel. It decides nothing about WHEN a
 * choice applies — it says what the filters would be, and the column applies
 * that at once while the sheet collects a batch.
 *
 * Each section is a group named by its heading, every option carries its count
 * in its accessible name, and an option with nothing left is disabled rather
 * than hidden, so nothing moves under the pointer.
 */
@Component({
  selector: 'storefront-filter-panel',
  imports: [TranslocoDirective, RouterLink, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './filter-panel.html',
  styleUrl: './filter-panel.css',
  host: { '[class.in-sheet]': "mode() === 'sheet'" },
})
export class FilterPanel {
  readonly facets = input.required<readonly SearchFacet[]>();
  readonly filters = input.required<ListingFilters>();
  readonly mode = input<'column' | 'sheet'>('column');
  /** A category page's sections, as links; null on a search, where the category is a filter. */
  readonly navigation = input<readonly CategoryLink[] | null>(null);
  readonly currency = input('EUR');

  /** The filters this choice would leave. */
  readonly changed = output<ListingFilters>();

  private readonly taxonomy = inject(Taxonomy);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);
  protected readonly links = inject(ShopLinks);

  protected readonly uid = `filters-${++panels}`;

  protected readonly sections = computed(() => {
    const sections = sectionsOf(this.facets());
    // On a category page the category is navigation, drawn from `navigation`.
    return this.navigation() ? sections.filter((section) => section.kind !== 'category') : sections;
  });

  /** Sections folded shut, by id. Every section opens unfolded. */
  protected readonly collapsed = signal<ReadonlySet<string>>(new Set());

  protected readonly brandsExpanded = signal(false);
  protected readonly brandFind = signal('');

  /** The boxes follow the URL until somebody types in them; typing unchooses the band, because the band is read from them. */
  protected readonly minText = linkedSignal(() => this.amountText(this.filters().minPrice));
  protected readonly maxText = linkedSignal(() => this.amountText(this.filters().maxPrice));

  // ---------- Reading ----------

  protected chosen(kind: FacetKind, value: string): boolean {
    return isChosen(this.filters(), kind, value);
  }

  protected dead(kind: FacetKind, option: FacetOption): boolean {
    return isDead(option, this.chosen(kind, option.value));
  }

  protected isOpen(id: string): boolean {
    return !this.collapsed().has(id);
  }

  protected brands(facet: SearchFacet) {
    return brandsToShow(facet.options, this.filters(), this.brandsExpanded(), this.brandFind());
  }

  protected categoryLevel(facet: SearchFacet) {
    return categoryLevel(facet, this.taxonomy.categories(), this.filters());
  }

  protected swatch(option: FacetOption): string | null {
    return safeSwatch(option.swatch);
  }

  /** A band is chosen when the boxes hold exactly its ends: the URL holds the range, never the band. */
  protected bandChosen(band: PriceBand): boolean {
    return parseAmount(this.minText()) === band.from && parseAmount(this.maxText()) === band.to;
  }

  protected bandLabel(band: PriceBand): string {
    const words = {
      upTo: (max: string) => this.transloco.translate('listing.price.upTo', { price: max }),
      from: (min: string) => this.transloco.translate('listing.price.from', { price: min }),
      between: (min: string, max: string) => this.transloco.translate('listing.price.between', { min, max }),
    };
    return priceLabel(band.from, band.to, (amount) => sayablePrice(amount, this.currency(), this.culture.active()), words);
  }

  // ---------- Choosing ----------

  protected toggleSection(id: string): void {
    this.collapsed.update((shut) => {
      const next = new Set(shut);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected toggle(kind: FacetKind, value: string): void {
    this.changed.emit(toggleOption(this.filters(), kind, value));
  }

  /** A band fills the boxes and applies; choosing the chosen band again takes it off. */
  protected chooseBand(band: PriceBand): void {
    const off = this.bandChosen(band);
    this.minText.set(off ? '' : this.amountText(band.from));
    this.maxText.set(off ? '' : this.amountText(band.to));
    this.changed.emit(withPrice(this.filters(), off ? null : band.from, off ? null : band.to));
  }

  /** *Ir*, or on the phone's sheet the box losing focus: the range typed. */
  protected applyRange(): void {
    const next = withPrice(this.filters(), parseAmount(this.minText()), parseAmount(this.maxText()));
    if (next.minPrice === this.filters().minPrice && next.maxPrice === this.filters().maxPrice) return;
    this.changed.emit(next);
  }

  private amountText(amount: number | null): string {
    return amount === null ? '' : String(amount);
  }
}
