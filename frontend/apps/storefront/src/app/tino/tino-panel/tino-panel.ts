import { ComposerBox } from '@tendero/shared-composer';
import { Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, type Params, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AnsweredProduct, SearchHit, TinoSearchLink } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { ConversationScroll, Icon, Notice, TurnWait } from '@tendero/shared-ui';
import { majorUnits } from '@tendero/shared-util';
import { filter, map } from 'rxjs';
import { listingQueryParams, NoFilters } from '../../listing/listing-filters';
import { ProductCard } from '../../product-card/product-card';
import { ShopLinks } from '../../shop-links';
import { contextOf } from '../tino-context';
import { TinoDoorStore } from '../tino-door.store';
import { TinoSource } from '../tino-source/tino-source';
import { type Exchange, TinoStore } from '../tino.store';

/** A product of an answer with its card, when the shop could still read one for its code. */
interface Named {
  readonly product: AnsweredProduct;
  readonly card: SearchHit | null;
}

/** An exchange as the panel draws it: its products with their cards, and where "see all" leads. */
interface Drawn {
  readonly exchange: Exchange;
  readonly named: readonly Named[];
  /** The card of the product a "its details do not say" is about, when the shop could read one. */
  readonly about: SearchHit | null;
  /** The search behind the answer as the listing is linked with, how many it holds, and whether only the nearest, when there is more to see. */
  readonly more: { readonly params: Params; readonly total: number; readonly nearest: boolean } | null;
}

/**
 * Tino's panel (phase 12b; the Tino spec, *The conversation* and *An answer*):
 * beside the page on a wide screen, a sheet on a phone. Nothing in it is prose
 * of a model's own. A product is the shop's card, read by its code; under it,
 * each sentence the model wrote with the chip of the fact it cites. When Tino
 * does not answer, the search's results for the same words, and a sentence that
 * says so (invariant 8). And where Tino is not available at all, that — and the
 * search box.
 */
@Component({
  selector: 'storefront-tino-panel',
  imports: [
    TranslocoDirective,
    RouterLink,
    Icon,
    Notice,
    ConversationScroll,
    TurnWait,
    ProductCard,
    TinoSource,
    ComposerBox,
  ],
  templateUrl: './tino-panel.html',
  // A product followed is a click on a link inside one of an answer's products: heard
  // here, on the host, as the home's modules hear theirs — the list items are not controls.
  host: { class: 'block', '(click)': 'clicked($event)' },
})
export class TinoPanel {
  protected readonly store = inject(TinoStore);
  protected readonly door = inject(TinoDoorStore);
  protected readonly links = inject(ShopLinks);
  protected readonly culture = inject(CultureStore).active;
  private readonly router = inject(Router);
  private readonly telemetry = inject(Telemetry);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** What the shopper is looking at, sent beside every question: a product's page, an order's, or nothing. */
  protected readonly context = computed(() => contextOf(this.url()));

  protected readonly drawn = computed<readonly Drawn[]>(() =>
    this.store.exchanges().map((exchange) => {
      const answer = exchange.answer;
      const named = (answer?.products ?? []).map((product) => ({
        product,
        card: exchange.cards.find((card) => card.code === product.code) ?? null,
      }));
      const about = exchange.cards.find((card) => card.code === answer?.noData?.code) ?? null;
      return { exchange, named, about, more: moreOf(exchange) };
    }),
  );

  constructor() {
    // What the search page handed over is asked as the panel opens.
    effect(() => {
      const handed = this.door.handed();
      if (!handed) return;
      untracked(() => {
        this.door.taken();
        this.store.ask(handed, this.context());
      });
    });
  }

  protected ask(question: string): void {
    this.store.ask(question, this.context());
  }

  /** The search, from the panel that could not answer: the shop's own box, where the words go as they always did. */
  protected find(text: string): void {
    const words = text.trim();
    if (!words) return;
    this.followed('search_opened');
    void this.router.navigate(this.links.home(), { queryParams: { q: words } });
    if (!this.door.wide()) this.door.close();
  }

  /**
   * A link of an answer was followed. On a phone the sheet covers the page it
   * leads to, so following closes it; beside the page it stays, and so does the
   * conversation.
   */
  protected followed(
    action: TrackedEvents['tino_result_followed']['action'],
    position = 0,
    card: TrackedEvents['tino_result_followed']['card'] = 'none',
  ): void {
    this.telemetry.trackEvent('tino_result_followed', { action, position, card });
    if (!this.door.wide()) this.door.close();
  }

  /**
   * A click on a link inside a product of an answer is that product followed, with
   * its place in the answer and whether its card carried the model's reasons or
   * only code's line: whether a card code added is ever opened (2026-10-21).
   */
  protected clicked(event: Event): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) return;
    const product = event.target.closest<HTMLElement>('[data-position]');
    const place = product?.dataset['position'];
    const card = product?.dataset['card'];
    if (place)
      this.followed('product_opened', Number(place), card === 'matched' || card === 'reasoned' ? card : 'none');
  }
}

/** Where "see all" leads for an exchange: the fallback's own search, or the one an answer's cards came from, as it was run. */
function moreOf(exchange: Exchange): Drawn['more'] {
  if (exchange.state === 'fallback')
    return exchange.found > exchange.cards.length
      ? {
          params: { q: exchange.answer?.searchText || exchange.question },
          total: exchange.found,
          nearest: exchange.nearest,
        }
      : null;

  // The server says where, and whether there is more than the cards: inside the
  // cards' category, with the price asked, counted as the listing will count it.
  const more = exchange.answer?.kind === 'Products' ? (exchange.answer.more ?? null) : null;
  return more ? { params: listingOf(more.search), total: more.total, nearest: false } : null;
}

/** A search as it was run, in the listing's own parameters: each filter one the shopper sees there and can take off. */
function listingOf(search: TinoSearchLink): Params {
  return {
    q: search.text,
    ...listingQueryParams({
      ...NoFilters,
      within: search.category ? [search.category] : [],
      brands: search.brand ? [search.brand] : [],
      minPrice: search.minPrice ? majorUnits(search.minPrice) : null,
      maxPrice: search.maxPrice ? majorUnits(search.maxPrice) : null,
      onOffer: search.onOffer,
      inStock: search.inStock,
    }),
  };
}
