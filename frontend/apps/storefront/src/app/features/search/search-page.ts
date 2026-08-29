import { Component, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { ProductSearchService } from '../../data-access/product-search.service';

type SearchState =
  | { status: 'idle' }
  | { status: 'searching' }
  | { status: 'done'; hits: SearchHit[]; total: number; tookMs: number; query: string }
  | { status: 'failed' };

/**
 * La home del storefront es la busqueda: lo primero que hace un tendero es
 * escuchar que buscas. Lo que se ve aqui es EXACTAMENTE lo que mide la puerta
 * de NDCG (tools/SearchEval) — mismo endpoint, mismo ranking.
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

  protected price(hit: SearchHit): string {
    return new Intl.NumberFormat(this.transloco.getActiveLang(), {
      style: 'currency',
      currency: hit.priceCurrency,
    }).format(hit.priceAmount);
  }
}
