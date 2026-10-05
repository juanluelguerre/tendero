import { Component, computed, effect, inject, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { AnsweredProduct, SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { Telemetry, type TrackedEvents } from '@tendero/shared-telemetry';
import { Composer, ConversationScroll, Icon, Notice } from '@tendero/shared-ui';
import { filter, map } from 'rxjs';
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
  /** What the search behind the answer was asked, and how many it found, when there is more to see. */
  readonly more: { readonly text: string; readonly total: number } | null;
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
  imports: [TranslocoDirective, RouterLink, Icon, Notice, Composer, ConversationScroll, ProductCard, TinoSource],
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
      return { exchange, named, more: moreOf(exchange) };
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
  protected followed(action: TrackedEvents['tino_result_followed']['action'], position = 0): void {
    this.telemetry.trackEvent('tino_result_followed', { action, position });
    if (!this.door.wide()) this.door.close();
  }

  /** A click on a link inside a product of an answer is that product followed, with its place in the answer. */
  protected clicked(event: Event): void {
    if (!(event.target instanceof Element) || !event.target.closest('a')) return;
    const place = event.target.closest<HTMLElement>('[data-position]')?.dataset['position'];
    if (place) this.followed('product_opened', Number(place));
  }
}

/** Where "see all" leads for an exchange: the fallback's own search, or the search an answer's products came from. */
function moreOf(exchange: Exchange): Drawn['more'] {
  if (exchange.state === 'fallback')
    return exchange.found > exchange.cards.length
      ? { text: exchange.answer?.searchText || exchange.question, total: exchange.found }
      : null;

  const answer = exchange.answer;
  if (answer?.kind !== 'Products') return null;

  // The search as the model asked it, which travels with the answer: its words are
  // the shop's search box's, and its total says whether there is more to see.
  const search = [...answer.reads].reverse().find((read) => read.tool === 'search_products');
  const text = (search?.arguments as { text?: unknown } | undefined)?.text;
  return search && typeof text === 'string' && (search.total ?? 0) > answer.products.length
    ? { text, total: search.total ?? 0 }
    : null;
}
