import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';
import { ProductSearchService } from '../../data-access/product-search.service';

type SearchState =
  | { status: 'idle' }
  | { status: 'searching' }
  | { status: 'done'; hits: SearchHit[]; total: number; tookMs: number; query: string }
  | { status: 'failed' };

/**
 * The storefront's home is the search: the first thing a shopkeeper does is
 * listen to what you are looking for. What is seen here is EXACTLY what the NDCG
 * gate measures (tools/SearchEval) — same endpoint, same ranking.
 */
@Component({
  selector: 'storefront-search-page',
  imports: [TranslocoDirective],
  templateUrl: './search-page.html',
  styleUrl: './search-page.css',
})
export class SearchPage {
  private readonly search = inject(ProductSearchService);
  private readonly transloco = inject(TranslocoService);

  protected readonly state = signal<SearchState>({ status: 'idle' });

  /**
   * Images the browser could not load. A real catalogue loses them constantly —
   * a CDN down, a deleted asset, a badly migrated URL — and a card showing the
   * broken-image icon looks worse than one with no photo. The repo's seed points
   * at cdn.example.com, which deliberately does not resolve, so this path is the
   * one you see on a fresh clone.
   */
  protected readonly broken = signal<ReadonlySet<string>>(new Set());

  protected onImageError(productId: string): void {
    this.broken.update((ids) => new Set(ids).add(productId));
  }

  protected submit(query: string): void {
    const text = query.trim();
    if (text.length < 2) {
      this.state.set({ status: 'idle' });
      return;
    }

    this.state.set({ status: 'searching' });

    this.search.search(text, this.transloco.getActiveLang()).subscribe({
      next: (page) =>
        this.state.set({
          status: 'done',
          hits: page.hits,
          total: page.total,
          tookMs: page.tookMs,
          query: text,
        }),
      error: () => this.state.set({ status: 'failed' }),
    });
  }

  /** The URL is composed here and does not come from the server: the index
   *  stores the key, so putting a CDN in front touches neither backend nor domain. */
  protected imageUrl(hit: SearchHit): string | null {
    return hit.imageId ? `/api/images/${hit.imageId}` : null;
  }

  protected price(hit: SearchHit): string {
    const culture = this.transloco.getActiveLang();

    // A product with several variants does NOT have a price, it has a range, and
    // showing only the matched variant's lies in both directions: it looks
    // expensive if the large size won and cheap if the small one did.
    // priceFrom/priceTo travel in the document itself (ADR 0015), so the card
    // needs no second call to say it.
    if (hit.priceFrom < hit.priceTo) {
      return `${formatPrice(hit.priceFrom, hit.priceCurrency, culture)} – ${formatPrice(
        hit.priceTo,
        hit.priceCurrency,
        culture,
      )}`;
    }

    return formatPrice(hit.priceFrom, hit.priceCurrency, culture);
  }
}
