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

  /**
   * Imagenes que el navegador no ha podido cargar. Un catalogo real las pierde
   * constantemente — CDN caido, activo borrado, URL mal migrada — y una ficha
   * con el icono de imagen rota se ve peor que una sin foto. El seed del repo
   * apunta a cdn.example.com, que no resuelve a proposito, asi que este camino
   * es el que se ve al arrancar recien clonado.
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

  /** La URL se compone aquí, no viene del servidor: el índice guarda la clave,
   *  así que meter un CDN delante no toca ni el backend ni el dominio. */
  protected imageUrl(hit: SearchHit): string | null {
    return hit.imageId ? `/api/images/${hit.imageId}` : null;
  }

  protected price(hit: SearchHit): string {
    return formatPrice(hit.priceAmount, hit.priceCurrency, this.transloco.getActiveLang());
  }
}
