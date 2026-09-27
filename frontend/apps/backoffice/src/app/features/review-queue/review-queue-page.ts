import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProductSummary } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { ProductReview } from '../../catalogue/product-review';
import { CatalogService } from '../../data-access/catalog.service';
import { BackofficeUsage } from '../../usage';
import { WorkPane } from '../../work/work-pane';

/** The laboratory's whole catalogue fits in one page of the list (the scale rule). */
const QueueSize = 100;

/**
 * The review queue: what is imported, or withdrawn, waits here in Draft, and
 * only publishing makes a product exist for a customer (ADR 0012).
 *
 * As R-3 drew the lists of work (R-4.3): Publish stays on the row, the likely
 * move, and a product opens beside the list with what publishing it would
 * publish — every culture as stored, the images, the attributes, the price —
 * because "check it and put it on sale" (R-0, B1) needs the checking. Importing
 * moved to the products screen: a developer's action was this screen's one clay
 * button whenever the queue was empty (R-1, B-F7).
 *
 * There is NO optimistic state: a row leaves when the server confirms, not
 * before. With the outbox in the middle a product takes a moment to reach the
 * index, and promising in the UI what is not true in search is worse than
 * waiting 200 ms.
 */
@Component({
  selector: 'backoffice-review-queue-page',
  imports: [RouterLink, TranslocoDirective, NgTemplateOutlet, WorkPane, ProductReview],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './review-queue-page.html',
  styleUrl: './review-queue-page.css',
})
export class ReviewQueuePage {
  private readonly catalog = inject(CatalogService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });
  protected readonly selectedCode = computed(() => this.params().get('product'));

  private readonly queue = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.catalog.list('draft', params.culture, 1, QueueSize),
  });

  protected readonly drafts = linkedSignal(() => this.queue.value()?.items ?? []);
  protected readonly loading = computed(() => this.queue.isLoading() && !this.queue.hasValue());
  protected readonly failed = computed(() => this.queue.status() === 'error' || this.publishFailed());

  protected readonly selected = computed(() => this.drafts().find((item) => item.code === this.selectedCode()) ?? null);

  protected readonly publishing = signal<ReadonlySet<string>>(new Set());
  private readonly publishFailed = signal(false);

  protected price(item: ProductSummary): string {
    return formatPrice(item.priceAmount, item.priceCurrency, this.culture.active());
  }

  protected open(item: ProductSummary): void {
    void this.router.navigate([], { queryParams: { product: item.code }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { product: null }, queryParamsHandling: 'merge' });
  }

  protected publish(item: ProductSummary): void {
    this.publishing.update((ids) => new Set(ids).add(item.productId));

    this.catalog
      .publish(item.productId)
      .pipe(this.usage.reported('review', 'publish'))
      .subscribe({
        next: () => {
          this.settle(item.productId);
          // It leaves the queue; the pane goes with it.
          this.drafts.update((items) => items.filter((candidate) => candidate.productId !== item.productId));
          if (this.selectedCode() === item.code) this.close();
        },
        error: (failure: unknown) => {
          this.settle(item.productId);
          if (isSessionExpired(failure)) return;
          this.publishFailed.set(true);
        },
      });
  }

  private settle(productId: string): void {
    this.publishing.update((ids) => {
      const next = new Set(ids);
      next.delete(productId);
      return next;
    });
  }
}
