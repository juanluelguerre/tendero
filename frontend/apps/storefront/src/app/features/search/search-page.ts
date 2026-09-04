import { Component, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { map } from 'rxjs';
import { AuthStore } from '@tendero/shared-auth';
import { CultureStore } from '@tendero/shared-i18n';
import type { SearchHit } from '@tendero/shared-api';
import { formatPrice } from '@tendero/shared-util';
import { CartStore } from '../../data-access/cart.service';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ShopLinks } from '../../shop-links';

/** Below this, a query matches so much that the answer is noise. */
const MinimumQueryLength = 2;

type SearchState =
  | { status: 'idle' }
  | { status: 'tooShort' }
  | { status: 'searching' }
  | {
      status: 'done';
      hits: SearchHit[];
      total: number;
      tookMs: number;
      query: string;
      /**
       * Whether this answer came from a language switch rather than from
       * somebody pressing search. It changes what an empty result MEANS: not
       * "we do not sell that" but "that query is in the other language", and
       * telling somebody to use fewer words when the real answer is "try it in
       * English" is worse than saying nothing.
       */
      afterLanguageChange: boolean;
    }
  | { status: 'failed' };

/**
 * The storefront's home is the search: the first thing a shopkeeper does is
 * listen to what you are looking for. What is seen here is EXACTLY what the NDCG
 * gate measures (tools/SearchEval) — same endpoint, same ranking.
 */
@Component({
  selector: 'storefront-search-page',
  imports: [TranslocoDirective, RouterLink],
  templateUrl: './search-page.html',
  styleUrl: './search-page.css',
})
export class SearchPage {
  /** Every link carries the language segment (P5-13). */
  protected readonly links = inject(ShopLinks);

  /** Whether to offer the way back to an order somebody already placed. */
  protected readonly signedIn = inject(AuthStore).isSignedIn;

  private readonly search = inject(ProductSearchService);
  private readonly culture = inject(CultureStore);
  private readonly transloco = inject(TranslocoService);

  /**
   * The last query that was actually sent, so switching language can re-run it.
   *
   * Without this, changing to English retranslates the chrome and leaves the
   * Spanish products underneath it — labels and results disagreeing on the same
   * page, which is worse than not offering the switch at all. The names, the
   * attribute text and the promotion reasons all come from the server in the
   * requested culture; only the server can answer again.
   */
  private readonly lastQuery = signal<string | null>(null);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /**
   * The query the URL is carrying. It is what makes a result page shareable,
   * bookmarkable and — once there is server rendering — crawlable, and its
   * absence was a row in the standing backlog: the query lived in a signal, so
   * two people could not look at the same results.
   *
   * It is also the half an agent needs. Handing a person a link to what it
   * found is the cheapest thing an agent surface can do, and it cannot do it
   * against a URL that says nothing.
   */
  protected readonly queryFromUrl = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('q') ?? '')),
    { initialValue: '' },
  );

  constructor() {
    effect(() => {
      const culture = this.culture.active();
      const query = untracked(() => this.lastQuery());

      if (query) this.run(query, culture, { afterLanguageChange: true });
    });

    // The URL is the source of truth for what is being searched, so back and
    // forward work without anything extra: the browser changes the parameter
    // and this runs. Typing into the box only navigates.
    effect(() => {
      const query = this.queryFromUrl();

      untracked(() => {
        if (query === (this.lastQuery() ?? '')) return;

        this.execute(query);
      });
    });
  }

  protected readonly state = signal<SearchState>({ status: 'idle' });

  /**
   * Images the browser could not load. A real catalogue loses them constantly —
   * a CDN down, a deleted asset, a badly migrated URL — and a card showing the
   * broken-image icon looks worse than one with no photo. The repo's seed points
   * at cdn.example.com, which deliberately does not resolve, so this path is the
   * one you see on a fresh clone.
   */
  protected readonly broken = signal<ReadonlySet<string>>(new Set());

  /**
   * Real queries from the golden set, not ones chosen because they look good.
   * The first thing a visitor clicks is then a query the NDCG gate actually
   * measures — if one of these ever stops returning something, the gate said so
   * before the shop did.
   *
   * They live in the translation files and not here because a suggestion IS a
   * query: offering "zapatillas running" to somebody reading an English page
   * would send a Spanish query to the English index, which is precisely the
   * mismatch phase 2 spent a whole phase removing.
   */
  protected readonly suggestions = toSignal(
    this.transloco
      .selectTranslateObject<string[]>('search.suggestions')
      // `selectTranslateObject` waits for the language file and re-emits on
      // every change; a computed over `translateObject` does neither, and read
      // during the switch it returned the KEY as a string. `@for` over a string
      // iterates characters, so the chips came out as s · e · a · r · c · h —
      // a failure with no error, no warning and nothing in the console, visible
      // only by looking at the page.
      .pipe(map((value) => (Array.isArray(value) ? value : []))),
    { initialValue: [] as string[] },
  );

  /** Four, because the grid is four wide at the width most people have. */
  protected readonly placeholders = [0, 1, 2, 3];

  /** Whether the search band should step back and let the results have the room. */
  protected hasResults(): boolean {
    const current = this.state();
    return current.status !== 'idle';
  }

  protected onImageError(productId: string): void {
    this.broken.update((ids) => new Set(ids).add(productId));
  }

  /**
   * Pressing search does not search: it navigates. The URL then carries the
   * query, and the effect above runs it — one path in, so the answer is the
   * same whether somebody typed it, pressed a suggestion, followed a link or
   * used the back button.
   */
  protected submit(query: string): void {
    const text = query.trim();

    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: text.length > 0 ? text : null },
      queryParamsHandling: 'merge',
      // A search is not a page in its own history entry: pressing back after
      // three refinements should leave the shop, not walk back through them.
      replaceUrl: true,
    });
  }

  private execute(query: string): void {
    const text = query.trim();

    // A single character is not a search, and saying so is not pedantry: the
    // alternative is pressing the button and getting nothing back, which reads
    // as a broken shop rather than as a query that was never sent.
    if (text.length === 0) {
      this.lastQuery.set(null);
      this.state.set({ status: 'idle' });
      return;
    }

    if (text.length < MinimumQueryLength) {
      this.lastQuery.set(null);
      this.state.set({ status: 'tooShort' });
      return;
    }

    this.lastQuery.set(text);
    this.run(text, this.culture.active(), { afterLanguageChange: false });
  }

  private run(text: string, culture: string, origin: { afterLanguageChange: boolean }): void {
    this.state.set({ status: 'searching' });

    this.search.search(text, culture).subscribe({
      next: (page) =>
        this.state.set({
          status: 'done',
          hits: page.hits,
          total: page.total,
          tookMs: page.tookMs,
          query: text,
          afterLanguageChange: origin.afterLanguageChange,
        }),
      error: () => this.state.set({ status: 'failed' }),
    });
  }

  /** The URL is composed here and does not come from the server: the index
   *  stores the key, so putting a CDN in front touches neither backend nor domain. */
  protected imageUrl(hit: SearchHit): string | null {
    return hit.imageId ? `/api/images/${hit.imageId}` : null;
  }

  /**
   * Which SKU is in flight. A per-card flag and not a page-wide one: pressing
   * add on one card must not grey out the rest of the results.
   */
  protected readonly adding = signal<string | null>(null);

  private readonly cart = inject(CartStore);

  /**
   * Adds the matched variant and leaves the shopper where they are.
   *
   * No redirect to the cart, deliberately. The header badge changes, which is
   * the confirmation, and a shop that threw you out of your results after every
   * add is a shop you buy one thing from.
   */
  protected async addToCart(hit: SearchHit): Promise<void> {
    this.adding.set(hit.matchedSku);

    try {
      await this.cart.add(hit.matchedSku);
    } finally {
      this.adding.set(null);
    }
  }

  protected price(hit: SearchHit): string {
    // The signal, not transloco.getActiveLang(): a plain read would not tell
    // change detection that the price has to be reformatted when the language —
    // and with it the decimal separator — changes.
    const culture = this.culture.active();

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
