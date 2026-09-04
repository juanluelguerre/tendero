import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import type { ImportResult, ProductSummary } from '@tendero/shared-api';
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
  templateUrl: './review-queue-page.html',
  styleUrl: './review-queue-page.css',
})
export class ReviewQueuePage {
  private readonly catalog = inject(CatalogService);
  private readonly culture = inject(CultureStore);

  protected readonly state = signal<QueueState>({ status: 'loading' });
  protected readonly publishing = signal<ReadonlySet<string>>(new Set());
  protected readonly importing = signal(false);

  /**
   * What the last import did, or null before one has run.
   *
   * The handler has always returned created, updated, failed and how long it
   * took — the screen threw all four away and left you looking at the same empty
   * state, with no way to tell a successful import of six products from a button
   * that did nothing.
   */
  protected readonly imported = signal<ImportResult | null>(null);

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
    this.imported.set(null);

    this.catalog.import().subscribe({
      next: (result) => {
        this.importing.set(false);
        this.imported.set(result);
        this.load();
      },
      error: () => {
        this.importing.set(false);
        this.state.set({ status: 'failed' });
      },
    });
  }

  /** One decimal. An import that took 1.24 s took a second and a bit. */
  protected seconds(result: ImportResult): string {
    return result.elapsedSeconds.toFixed(1);
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
