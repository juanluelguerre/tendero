import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, translateObjectSignal } from '@jsverse/transloco';
import type { ImportResult, ProductStatus, ProductSummary } from '@tendero/shared-api';
import { isSessionExpired } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
import { type Observable } from 'rxjs';
import { ProductReview } from '../../catalogue/product-review';
import { CatalogService } from '../../data-access/catalog.service';
import { BackofficeUsage } from '../../usage';
import { FilterChips, type FilterOption } from '../../work/filter-chips';
import { Undo } from '../../work/undo';
import { WorkPane } from '../../work/work-pane';

/** Twenty rows a page, the queue's number: the same table, dense. */
const PageSize = 20;

const Statuses = ['active', 'draft', 'archived'] as const;

/**
 * Every product, by status, and what can be done with one (the redesign's
 * R-4.3): published from here or taken back to review, with its variants one
 * link away. It opens on what is on sale.
 *
 * **Withdrawing is undoable, so it does not ask**: it offers Undo, which is
 * publishing again — the domain's own reverse, audited like any other move
 * (R-2, P12). Importing lives here now, as a quiet action: it is the
 * catalogue's, and it was the review queue's clay button whenever that queue
 * was empty (R-1, B-F7).
 */
@Component({
  selector: 'backoffice-catalogue-page',
  imports: [RouterLink, TranslocoDirective, NgTemplateOutlet, WorkPane, FilterChips, ProductReview],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalogue-page.html',
  styleUrl: './catalogue-page.css',
})
export class CataloguePage {
  private readonly catalog = inject(CatalogService);
  private readonly usage = inject(BackofficeUsage);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);
  private readonly undo = inject(Undo);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private readonly filterLabels = translateObjectSignal('catalogue.filter');
  private readonly params = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  protected readonly statusKey = computed(() => this.params().get('status') ?? 'active');
  protected readonly selectedCode = computed(() => this.params().get('product'));
  protected readonly page = computed(() => Math.max(1, Number(this.params().get('page') ?? 1) || 1));

  private readonly list = rxResource({
    params: () => ({ status: this.statusKey(), page: this.page(), culture: this.culture.active() }),
    stream: ({ params }) =>
      this.catalog.list(params.status === 'all' ? null : (params.status as ProductStatus), params.culture, params.page, PageSize),
  });

  protected readonly products = linkedSignal(() => this.list.value()?.items ?? []);
  protected readonly loading = computed(() => this.list.isLoading() && !this.list.hasValue());
  protected readonly failed = computed(() => this.list.status() === 'error');
  protected readonly pages = computed(() => Math.max(1, Math.ceil((this.list.value()?.total ?? 0) / PageSize)));

  /** The count is known for the filter showing, and said there only. */
  protected readonly filters = computed<FilterOption[]>(() => {
    const labels = this.filterLabels() as Record<string, string>;
    const total = this.list.value()?.total ?? null;
    return [...Statuses, 'all'].map((key) => ({ key, label: labels[key] ?? key, count: key === this.statusKey() ? total : null }));
  });

  protected readonly selected = computed(() => this.products().find((item) => item.code === this.selectedCode()) ?? null);

  protected readonly busy = signal<string | null>(null);
  protected readonly refusal = signal<string | null>(null);
  protected readonly importing = signal(false);

  /**
   * What the last import did. The handler returns created, updated, failed and
   * how long it took: "it worked" and "it created ninety-four products" are
   * different amounts of information, and the second is the answer.
   */
  protected readonly imported = signal<ImportResult | null>(null);

  protected price(item: ProductSummary): string {
    return formatPrice(item.priceAmount, item.priceCurrency, this.culture.active());
  }

  protected seconds(result: ImportResult): string {
    return result.elapsedSeconds.toFixed(1);
  }

  protected filterBy(status: string): void {
    void this.router.navigate([], { queryParams: { status: status === 'active' ? null : status, page: null }, queryParamsHandling: 'merge' });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page: page <= 1 ? null : Math.min(page, this.pages()) }, queryParamsHandling: 'merge' });
  }

  protected open(item: ProductSummary): void {
    void this.router.navigate([], { queryParams: { product: item.code }, queryParamsHandling: 'merge' });
  }

  protected close(): void {
    void this.router.navigate([], { queryParams: { product: null }, queryParamsHandling: 'merge' });
  }

  protected publish(item: ProductSummary): void {
    this.change(item, this.catalog.publish(item.productId).pipe(this.usage.reported('catalogue', 'publish')), 'active');
  }

  protected withdraw(item: ProductSummary): void {
    this.change(item, this.catalog.withdraw(item.productId).pipe(this.usage.reported('catalogue', 'withdraw')), 'draft', () =>
      this.undo.offerTo(this.transloco.translate('catalogue.withdrawn', { name: item.name }), () => this.publish({ ...item, status: 'draft' })),
    );
  }

  protected importSeed(): void {
    this.importing.set(true);
    this.imported.set(null);

    this.catalog
      .import()
      .pipe(this.usage.reported('catalogue', 'import'))
      .subscribe({
        next: (result) => {
          this.importing.set(false);
          this.imported.set(result);
          this.list.reload();
        },
        error: (failure: unknown) => {
          this.importing.set(false);
          if (isSessionExpired(failure)) return;
          this.refusal.set('');
        },
      });
  }

  /** One change at a time; the row takes the status the server answered with. */
  private change(item: ProductSummary, request: Observable<{ status: string }>, fallback: string, done?: () => void): void {
    this.busy.set(item.productId);
    this.refusal.set(null);

    request.subscribe({
      next: (answer) => {
        this.busy.set(null);
        const status = answer.status ?? fallback;
        this.products.update((items) => items.map((row) => (row.productId === item.productId ? { ...row, status } : row)));
        done?.();
      },
      error: (failure: unknown) => {
        this.busy.set(null);
        if (isSessionExpired(failure)) return;
        this.refusal.set('');
      },
    });
  }
}
