import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import type { ProductSummary } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';
import { CatalogService } from '../../data-access/catalog.service';

type QueueState =
  | { status: 'loading' }
  | { status: 'ready'; items: ProductSummary[]; total: number }
  | { status: 'failed' };

/**
 * Cola de revision: lo que se importa entra en Draft y solo lo Active se indexa,
 * asi que este es el paso que decide si un producto existe para el cliente.
 *
 * Hasta ahora era un estado vacio fijo cuyo texto de ayuda pedia lanzar un curl.
 * Publicar desde aqui no es azucar: mientras la unica forma de hacerlo sea la
 * linea de comandos, la revision humana no es parte del producto, es una nota al
 * pie. Ese es literalmente el articulo de la fase 2.
 *
 * NO hay estado optimista, y es deliberado: la fila desaparece cuando el
 * servidor confirma, no antes. Con el Outbox de por medio el producto tarda un
 * instante en aparecer en el indice, y prometer en la UI algo que todavia no es
 * cierto en la busqueda es peor que esperar 200 ms.
 */
@Component({
  selector: 'backoffice-review-queue-page',
  imports: [TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <header class="head">
        <h1 class="title">{{ t('reviewQueue.title') }}</h1>
        @if (state().status === 'ready' && pending() > 0) {
          <span class="count numeric">{{ t('reviewQueue.pending', { count: pending() }) }}</span>
        }
      </header>

      @switch (state().status) {
        @case ('loading') {
          <p class="muted" role="status" aria-live="polite">{{ t('reviewQueue.loading') }}</p>
        }
        @case ('failed') {
          <p class="failed" role="alert">{{ t('reviewQueue.failed') }}</p>
        }
        @case ('ready') {
          @let ready = state();
          @if (ready.status === 'ready') {
            @if (ready.items.length === 0) {
              <div class="empty">
                <p class="empty__text">{{ t('reviewQueue.empty') }}</p>
                <!-- La unica accion clay de la vista (design/DESIGN.md). -->
                <button type="button" class="publish" [disabled]="importing()" (click)="importSeed()">
                  {{ importing() ? t('reviewQueue.importing') : t('reviewQueue.import') }}
                </button>
              </div>
            } @else {
              <table class="grid">
                <caption class="sr-only">{{ t('reviewQueue.title') }}</caption>
                <thead>
                  <tr>
                    <th scope="col">{{ t('reviewQueue.column.product') }}</th>
                    <th scope="col">{{ t('reviewQueue.column.brand') }}</th>
                    <th scope="col" class="right">{{ t('reviewQueue.column.price') }}</th>
                    <th scope="col">{{ t('reviewQueue.column.languages') }}</th>
                    <th scope="col"><span class="sr-only">{{ t('reviewQueue.column.action') }}</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (item of ready.items; track item.productId) {
                    <tr>
                      <td>
                        <span class="name">{{ item.name }}</span>
                        <span class="slug numeric">{{ item.slug }}</span>
                      </td>
                      <td>{{ item.brand ?? '—' }}</td>
                      <td class="right numeric">{{ price(item) }}</td>
                      <td>
                        @if (item.missingCultures.length === 0) {
                          <!-- Color nunca solo: siempre con palabra (design/DESIGN.md). -->
                          <span class="tag tag--ok">{{ t('reviewQueue.complete') }}</span>
                        } @else {
                          <span class="tag tag--warn">{{
                            t('reviewQueue.missing', { cultures: item.missingCultures.join(', ') })
                          }}</span>
                        }
                      </td>
                      <td class="right">
                        <button
                          type="button"
                          class="publish"
                          [disabled]="publishing().has(item.productId)"
                          (click)="publish(item)"
                        >
                          {{ publishing().has(item.productId) ? t('reviewQueue.publishing') : t('reviewQueue.publish') }}
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          }
        }
      }
    </ng-container>
  `,
  styles: `
    .head { display: flex; align-items: baseline; gap: var(--space-3); margin-block-end: var(--space-5); }
    .title { font-family: var(--font-display); font-size: var(--text-lg); margin: 0; }
    .count { font-size: var(--text-2xs); color: var(--text-muted); }
    .muted { color: var(--text-muted); }
    .failed { color: var(--danger); }

    .empty { border: 1px dashed var(--border); border-radius: var(--radius-lg); padding: var(--space-7); text-align: center; }
    .empty__text { color: var(--text-muted); margin: 0 0 var(--space-4); }

    .grid { width: 100%; border-collapse: collapse; font-size: var(--text-xs); }
    .grid th, .grid td { text-align: start; padding: 0 var(--space-3); height: 36px; border-block-end: 1px solid var(--border); }
    .grid th { font-weight: 600; color: var(--text-muted); font-size: var(--text-2xs); letter-spacing: var(--tracking-wide); text-transform: uppercase; }
    .right { text-align: end; }
    .name { display: block; color: var(--text); }
    .slug { display: block; font-size: var(--text-2xs); color: var(--text-subtle); }

    .tag { display: inline-block; padding: 1px var(--space-2); border-radius: var(--radius-sm); font-size: var(--text-2xs); }
    .tag--ok { color: var(--positive); border: 1px solid var(--positive); }
    .tag--warn { color: var(--warning); border: 1px solid var(--warning); }

    .publish { font: inherit; color: var(--stone-0); background: var(--accent); border: 0; border-radius: var(--radius-sm); padding: var(--space-2) var(--space-4); cursor: pointer; }
    .publish:hover:not(:disabled) { background: var(--accent-hover); }
    .publish:disabled { opacity: 0.6; cursor: default; }
    .publish:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }

    .sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  `,
})
export class ReviewQueuePage {
  private readonly catalog = inject(CatalogService);
  private readonly transloco = inject(TranslocoService);

  protected readonly state = signal<QueueState>({ status: 'loading' });
  protected readonly publishing = signal<ReadonlySet<string>>(new Set());
  protected readonly importing = signal(false);

  protected readonly pending = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? current.total : 0;
  });

  constructor() {
    this.load();
  }

  protected publish(item: ProductSummary): void {
    this.publishing.update((ids) => new Set(ids).add(item.productId));

    this.catalog.publish(item.productId).subscribe({
      next: () => {
        this.clearPublishing(item.productId);
        // Recargar en vez de quitar la fila a mano: la cola es del servidor y
        // otro revisor puede haber publicado algo mientras tanto.
        this.load();
      },
      error: () => {
        this.clearPublishing(item.productId);
        this.state.set({ status: 'failed' });
      },
    });
  }

  protected importSeed(): void {
    this.importing.set(true);
    this.catalog.import().subscribe({
      next: () => {
        this.importing.set(false);
        this.load();
      },
      error: () => {
        this.importing.set(false);
        this.state.set({ status: 'failed' });
      },
    });
  }

  protected price(item: ProductSummary): string {
    return formatPrice(item.priceAmount, item.priceCurrency, this.transloco.getActiveLang());
  }

  private load(): void {
    this.state.set({ status: 'loading' });

    this.catalog.list('draft', this.transloco.getActiveLang()).subscribe({
      next: (page) => this.state.set({ status: 'ready', items: page.items, total: page.total }),
      error: () => this.state.set({ status: 'failed' }),
    });
  }

  private clearPublishing(productId: string): void {
    this.publishing.update((ids) => {
      const next = new Set(ids);
      next.delete(productId);
      return next;
    });
  }
}
