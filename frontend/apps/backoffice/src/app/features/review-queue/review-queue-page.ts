import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { ProductSummary } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';
import { CatalogService } from '../../data-access/catalog.service';

type QueueState =
  | { status: 'loading' }
  | { status: 'ready'; items: ProductSummary[]; total: number }
  | { status: 'failed' };

/**
 * The review queue: what gets imported lands in Draft and only Active is
 * indexed, so this is the step that decides whether a product exists for a
 * customer.
 *
 * Until now it was a fixed empty state whose help text asked you to run a curl.
 * Publishing from here is not sugar: while the only way to do it is the command
 * line, human review is not part of the product, it is a footnote. That is
 * literally phase 2's article.
 *
 * There is NO optimistic state, and that is deliberate: the row disappears when
 * the server confirms, not before. With the Outbox in the middle the product
 * takes a moment to appear in the index, and promising something in the UI that
 * is not true in search yet is worse than waiting 200 ms.
 */
@Component({
  selector: 'backoffice-review-queue-page',
  imports: [RouterLink, TranslocoDirective],
  template: `
    <ng-container *transloco="let t">
      <header class="page-head">
        <h1 class="page-title">{{ t('reviewQueue.title') }}</h1>
        @if (state().status === 'ready' && pending() > 0) {
          <span class="tag tag--warn">{{ t('reviewQueue.pending', { count: pending() }) }}</span>
        }
        @if (state().status === 'ready' && pending() > 0) {
          <div class="page-actions">
            <button type="button" class="button button--quiet" [disabled]="importing()"
                    (click)="importSeed()">
              {{ importing() ? t('reviewQueue.importing') : t('reviewQueue.import') }}
            </button>
          </div>
        }
      </header>
      <p class="page-hint">{{ t('reviewQueue.hint') }}</p>

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
                <!-- The view's only clay action (design/DESIGN.md). -->
                <button type="button" class="button" [disabled]="importing()" (click)="importSeed()">
                  {{ importing() ? t('reviewQueue.importing') : t('reviewQueue.import') }}
                </button>
              </div>
            } @else {
              <div class="table-wrap">
              <table class="table">
                <caption class="sr-only">{{ t('reviewQueue.title') }}</caption>
                <thead>
                  <tr>
                    <th scope="col">{{ t('reviewQueue.column.product') }}</th>
                    <th scope="col">{{ t('reviewQueue.column.brand') }}</th>
                    <th scope="col" class="right">{{ t('reviewQueue.column.price') }}</th>
                    <th scope="col">{{ t('reviewQueue.column.languages') }}</th>
                    <th scope="col"><span class="sr-only">{{ t('variants.title') }}</span></th>
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
                          <!-- Colour never alone: always with a word (design/DESIGN.md). -->
                          <span class="tag tag--ok">{{ t('reviewQueue.complete') }}</span>
                        } @else {
                          <span class="tag tag--warn">{{
                            t('reviewQueue.missing', { cultures: item.missingCultures.join(', ') })
                          }}</span>
                        }
                      </td>
                      <!-- Defining variants is a catalogue decision, not an
                           import one, so it is offered per row and not in bulk. -->
                      <td>
                        <a class="quiet-link" [routerLink]="['/products', item.productId, 'variants']">
                          {{ t('variants.title') }}
                        </a>
                      </td>
                      <td class="right">
                        <button
                          type="button"
                          class="button"
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
              </div>
            }
          }
        }
      }
    </ng-container>
  `,
  styles: `
    /* Only what this page adds. The page header, the table, the tags, the
       buttons and the empty state are primitives in styles.css: they were each
       copied between three screens before they moved there. */
    .name { display: block; color: var(--text); font-weight: 600; }
    .slug { display: block; margin-block-start: 2px; font-size: var(--text-3xs); color: var(--text-subtle); }

    .quiet-link { font-size: var(--text-xs); color: var(--text-muted); }
    .quiet-link:hover { color: var(--accent); }

    /* The action column stays as narrow as its button and is pinned right, so a
       queue of twenty rows has one straight edge to aim at. */
    .table td:last-child { width: 1%; white-space: nowrap; }
  `,
})
export class ReviewQueuePage {
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly state = signal<QueueState>({ status: 'loading' });
  protected readonly publishing = signal<ReadonlySet<string>>(new Set());
  protected readonly importing = signal(false);

  protected readonly pending = computed(() => {
    const current = this.state();
    return current.status === 'ready' ? current.total : 0;
  });

  constructor() {
    // Product names come from the server resolved into the requested culture, so
    // the queue is reloaded when the language changes. Without it the chrome
    // switches to English and the rows stay Spanish — and "missing cultures",
    // the one fact this screen exists for, would be computed against the wrong
    // one.
    effect(() => {
      this.culture.active();
      untracked(() => this.load());
    });
  }

  protected publish(item: ProductSummary): void {
    this.publishing.update((ids) => new Set(ids).add(item.productId));

    this.catalog.publish(item.productId).subscribe({
      next: () => {
        this.clearPublishing(item.productId);
        // Reload rather than removing the row by hand: the queue belongs to the
        // server, and another reviewer may have published something meanwhile.
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
    return formatPrice(item.priceAmount, item.priceCurrency, this.culture.active());
  }

  private load(): void {
    this.state.set({ status: 'loading' });

    this.catalog.list('draft', this.culture.active()).subscribe({
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
