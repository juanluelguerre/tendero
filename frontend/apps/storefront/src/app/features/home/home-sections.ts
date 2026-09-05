import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { formatDateTime } from '@tendero/shared-util';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ShopFrontService, type Category, type Offer } from '../../data-access/shop-front.service';
import { ProductCard } from '../../product-card';
import { ShopLinks } from '../../shop-links';
import { departmentArt } from '../../shop-art';

/**
 * The front of the shop: what somebody sees before they have typed anything.
 *
 * **Three bands, and each one answers a question a shopper actually has** —
 * what do you sell, what is cheap right now, what is new. There is no fourth
 * band of best sellers, and its absence is a decision rather than an oversight:
 * that needs orders, and a ranking computed from three of them is a lie with a
 * chart on it. It arrives when the data does.
 *
 * The reference was Apple rather than Amazon, and the difference is worth
 * stating because it decided the layout. Apple gives one idea a whole band and
 * lets whitespace do the work; Amazon gives you somewhere to go. Apple can do
 * that because it sells ten things — a hundred products across sixteen
 * categories needs the way in that Amazon is good at. So: Apple's restraint,
 * Amazon's findability, and none of Amazon's density.
 *
 * It renders inside the search page rather than on a route of its own, because
 * `/es` already IS the search page and the query lives in the URL. A separate
 * home route would mean two places that answer the same address.
 */
@Component({
  selector: 'storefront-home-sections',
  imports: [TranslocoDirective, RouterLink, ProductCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home-sections.html',
  styleUrl: './home-sections.css',
})
export class HomeSections {
  private readonly shopFront = inject(ShopFrontService);
  private readonly search = inject(ProductSearchService);
  private readonly culture = inject(CultureStore);

  protected readonly links = inject(ShopLinks);

  private readonly categories = signal<Category[]>([]);

  protected readonly offers = signal<Offer[]>([]);
  protected readonly newest = signal<SearchHit[]>([]);

  /**
   * The departments: the roots of the taxonomy, which is exactly what a shop
   * means by the word. There is no second concept to model — Amazon's
   * "departments" are its top-level categories, and inventing a parallel one
   * here would be two trees to keep in step.
   */
  protected readonly departments = computed(() =>
    this.categories().filter((category) => category.depth === 0),
  );

  /**
   * A few of the sections inside a department, as one line.
   *
   * Built here and not in the template: an arrow function in an Angular
   * expression is not valid syntax, and pushing the join into the component is
   * where it belonged anyway — the template says what appears, not how the
   * string was made.
   */
  protected sectionNames(department: Category): string {
    return this.categories()
      .filter((category) => category.parent === department.code)
      .slice(0, 4)
      .map((category) => category.name)
      .join(' · ');
  }

  /** The department's illustration. Every root has one; anything else falls
   *  back to the shop's own mark rather than to a broken tile. */
  protected art(department: Category): string {
    return departmentArt(department.code);
  }

  /** The end date in the reader's own format. `04/09/2026` and `09/04/2026` are
   *  the same day and different months, so the culture decides. */
  protected until(offer: Offer): string {
    return offer.until ? formatDateTime(offer.until, this.culture.active()) : '';
  }

  constructor() {
    // Re-run on a language switch: the category names, the offer labels and the
    // product names all come from the server in the requested culture, and a
    // home page that only retranslated its headings would show English bands
    // over Spanish rows.
    effect(() => this.load(this.culture.active()));
  }

  private load(culture: string): void {
    // Three independent requests, and a failure in any one of them takes only
    // its own band away. A home page that renders nothing because the promotions
    // reader is down is a shop that closes over a discount.
    this.shopFront.categories(culture).subscribe({
      next: (result) => this.categories.set(result.items),
      error: () => this.categories.set([]),
    });

    this.shopFront.offers(culture).subscribe({
      next: (result) => this.offers.set(result.items),
      error: () => this.offers.set([]),
    });

    this.search.browse({ culture, sort: 'newest', pageSize: 8 }).subscribe({
      next: (page) => this.newest.set(page.hits),
      error: () => this.newest.set([]),
    });
  }
}
