import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective } from '@jsverse/transloco';
import { map, type Subscription } from 'rxjs';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ShopFrontService, type Category } from '../../data-access/shop-front.service';
import { ProductCard } from '../../product-card';
import { ShopLinks } from '../../shop-links';
import { departmentArt, rootOf } from '../../shop-art';

type PageState =
  | { status: 'loading' }
  | { status: 'ready'; hits: SearchHit[]; total: number }
  | { status: 'unknown' }
  | { status: 'failed' };

/** Enough to fill the grid without asking for the whole department. */
const PageSize = 24;

/**
 * Everything in one branch of the taxonomy.
 *
 * **It is the search endpoint with no words in it**, and that is the point
 * rather than a shortcut: a shop with a second way of listing products has one
 * that the relevance gate measures and one that nobody checks. The filter is on
 * the branch, so "Hogar" answers with the fifty-five things under it and not
 * with the nothing filed directly against it.
 *
 * The code is in the URL and the name is not, for the reason ADR 0026 gives
 * about products: a name is not stable, and this one exists in two languages.
 */
@Component({
  selector: 'storefront-category-page',
  imports: [TranslocoDirective, RouterLink, ProductCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './category-page.html',
  styleUrl: './category-page.css',
})
export class CategoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly search = inject(ProductSearchService);
  private readonly shopFront = inject(ShopFrontService);
  private readonly culture = inject(CultureStore);

  protected readonly links = inject(ShopLinks);
  protected readonly state = signal<PageState>({ status: 'loading' });

  /** The category itself, for the heading and the breadcrumb. */
  protected readonly category = signal<Category | null>(null);

  /** Its children, so a department offers the sections inside it. */
  protected readonly children = signal<Category[]>([]);

  /**
   * The department's illustration, borrowed by everything under it.
   *
   * `COFFEE_MAKER` does not get a drawing of its own — it gets Hogar's, because
   * that is the thing a shopper recognises and because art per leaf is
   * twenty-three drawings to keep in step with a taxonomy that changes.
   */
  protected readonly art = signal<string | null>(null);

  /** The page already asked for. `Ver más` appends rather than replacing,
   *  because a shopper scrolling a department has not finished reading the
   *  rows above the button. */
  private readonly page = signal(1);

  protected readonly loadingMore = signal(false);

  private readonly code = toSignal(
    this.route.paramMap.pipe(map((params) => params.get('code') ?? '')),
    { initialValue: '' },
  );

  /**
   * The request being answered, so a change of code or language cancels it
   * rather than racing it: without this the answer that arrived LAST won,
   * whichever had been asked for last.
   */
  private inFlight?: Subscription;
  private appending?: Subscription;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.inFlight?.unsubscribe();
      this.appending?.unsubscribe();
    });

    effect(() => {
      const code = this.code();
      const culture = this.culture.active();

      if (!code) return;

      this.inFlight?.unsubscribe();
      this.appending?.unsubscribe();
      this.loadingMore.set(false);
      this.state.set({ status: 'loading' });

      // The taxonomy first, because a code nobody recognises is a 404 rather
      // than an empty grid — "no products here" and "no such category" are
      // different answers and only one of them is the shopper's fault.
      this.inFlight = this.shopFront.categories(culture).subscribe({
        next: (result) => {
          const found = result.items.find(
            (item) => item.code.toLowerCase() === code.toLowerCase(),
          );

          this.category.set(found ?? null);
          this.children.set(
            found ? result.items.filter((item) => item.parent === found.code) : [],
          );
          this.art.set(
            found ? departmentArt(rootOf(found.code, result.items)) : null,
          );

          if (!found) {
            this.state.set({ status: 'unknown' });
            return;
          }

          this.page.set(1);

          this.inFlight = this.search
            .browse({ culture, category: found.code, pageSize: PageSize })
            .subscribe({
              next: (page) => this.state.set({ status: 'ready', hits: page.hits, total: page.total }),
              error: () => this.state.set({ status: 'failed' }),
            });
        },
        error: () => this.state.set({ status: 'failed' }),
      });
    });
  }

  /**
   * The next page, appended.
   *
   * **A department of fifty-five products that shows twenty-four and stops is
   * a shop with thirty-one things you cannot reach.** The count above the grid
   * was already saying so out loud.
   */
  protected loadMore(): void {
    const current = this.state();
    const category = this.category();
    if (current.status !== 'ready' || !category || this.loadingMore()) return;

    const next = this.page() + 1;
    this.loadingMore.set(true);

    this.appending = this.search
      .browse({
        culture: this.culture.active(),
        category: category.code,
        page: next,
        pageSize: PageSize,
      })
      .subscribe({
        next: (page) => {
          this.page.set(next);
          this.loadingMore.set(false);
          this.state.set({
            status: 'ready',
            hits: [...current.hits, ...page.hits],
            total: page.total,
          });
        },
        // Nothing is lost and nothing is claimed: the rows already read stay,
        // and the button comes back so it can be tried again.
        error: () => this.loadingMore.set(false),
      });
  }

  protected hasMore(): boolean {
    const current = this.state();
    return current.status === 'ready' && current.hits.length < current.total;
  }
}
