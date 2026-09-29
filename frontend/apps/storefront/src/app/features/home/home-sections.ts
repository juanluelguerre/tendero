import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { map, of, switchMap } from 'rxjs';
import { CampaignCard } from '../../campaigns/campaign-card';
import { Campaigns } from '../../campaigns/campaigns';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ReturnsPolicy } from '../../data-access/returns-policy.service';
import { ShopFrontService } from '../../data-access/shop-front.service';
import { Taxonomy } from '../../data-access/taxonomy';
import { WhatSellsService } from '../../data-access/what-sells.service';
import { AgentCard } from '../../shelves/agent-card';
import { ProductRow, type ShelfItem } from '../../shelves/product-row';
import { PromiseStrip } from '../../shelves/promise-strip';
import { departmentArt } from '../../shop-art';
import { ShopLinks } from '../../shop-links';
import { Trail } from '../../trail/trail';
import { TrailRow } from '../../trail/trail-row';
import {
  departmentsOf,
  freeShippingOf,
  labelled,
  offerRulesOf,
  rankedIds,
  rankedItems,
  risingItems,
  suggestionItems,
} from './home-bands';
import { HomeModule } from './home-module';

/** How many cards a row asks for: two screens of a desktop row, one of them whole. */
const RowSize = 10;
/** *Lo más vendido* is a top five. */
const TopSize = 5;

/**
 * The front of the shop, top to bottom (R-8, docs/specs/storefront-home.md; the
 * approved mock-ups): the campaign that leads, *Sigue donde lo dejaste*, *Compra
 * con tu agente*, *Ofertas*, *Puede que te guste*, *Tendencias*, *Lo más
 * vendido*, *Novedades*, the departments and *La tienda promete*.
 *
 * **Each band fails alone.** Every one is its own request, as a resource, and a
 * band whose request failed or answered nothing is simply not drawn — a home
 * that renders nothing because the rankings are down is a shop that closes over
 * a chart. The rows' cards load when they come near the screen (`ProductRow`).
 *
 * **The page composes; the rules are elsewhere**: which campaign leads is the
 * server's order (`leadingCampaign`), what a row says about each product is
 * `home-bands.ts`, what the trail keeps is `Trail`'s.
 *
 * It renders inside the search page rather than on a route of its own, because
 * `/es` already IS the search page and the query lives in the URL. The header's
 * box is the shop's one search, so the home no longer asks *¿Qué estás
 * buscando?* — a campaign is a better first sentence than a question the header
 * already asks (the spec).
 */
@Component({
  selector: 'storefront-home-sections',
  imports: [TranslocoDirective, RouterLink, CampaignCard, TrailRow, AgentCard, ProductRow, PromiseStrip, HomeModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home-sections.html',
  styleUrl: './home-sections.css',
})
export class HomeSections {
  private readonly search = inject(ProductSearchService);
  private readonly sales = inject(WhatSellsService);
  private readonly shopFront = inject(ShopFrontService);
  private readonly culture = inject(CultureStore);
  private readonly taxonomy = inject(Taxonomy);

  protected readonly links = inject(ShopLinks);
  protected readonly campaigns = inject(Campaigns);
  protected readonly trail = inject(Trail);
  protected readonly returnDays = inject(ReturnsPolicy).windowDays;

  // ---------- The campaign ----------

  protected readonly campaign = this.campaigns.leading;

  /** What the leading campaign reaches, for its card when it has no picture of its own. */
  private readonly campaignProducts = rxResource({
    params: () => {
      const campaign = this.campaign();
      return campaign && !campaign.imageId && campaign.phase === 'Running'
        ? { culture: this.culture.active(), campaign: campaign.code }
        : undefined;
    },
    stream: ({ params }) => this.search.browse({ ...params, pageSize: 3 }).pipe(map((page) => page.hits)),
  });

  protected readonly campaignHits = computed<readonly SearchHit[]>(() =>
    this.campaignProducts.hasValue() ? this.campaignProducts.value() : [],
  );

  // ---------- The trail and what it suggests ----------

  private readonly viewedCodes = computed(() => this.trail.shown().views.map((entry) => entry.key));

  private readonly viewedCards = rxResource({
    params: () => (this.viewedCodes().length ? { culture: this.culture.active(), codes: this.viewedCodes() } : undefined),
    stream: ({ params }) => this.search.cards(params.culture, { codes: params.codes }).pipe(map((result) => result.items)),
  });

  protected readonly viewed = computed<readonly SearchHit[]>(() =>
    this.viewedCards.hasValue() ? this.viewedCards.value() : [],
  );

  private readonly similar = rxResource({
    params: () => (this.viewedCodes().length ? { culture: this.culture.active(), codes: this.viewedCodes() } : undefined),
    stream: ({ params }) => this.search.similar(params.culture, params.codes, RowSize),
  });

  /** *Porque viste {{name}}*, in the reader's language. */
  protected because(t: (key: string, params?: Record<string, unknown>) => string): (name: string) => string {
    return (name) => t('home.because', { name });
  }

  /** *Porque viste…*: the template hands in the sentence, in the reader's language. */
  protected suggested(because: (name: string) => string): ShelfItem[] {
    return this.similar.hasValue() ? suggestionItems(this.similar.value(), this.viewed(), because) : [];
  }

  // ---------- Offers ----------

  private readonly offerHits = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.search.browse({ culture: params.culture, onOffer: true, pageSize: RowSize }),
  });

  private readonly offerRuleList = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.shopFront.offers(params.culture).pipe(map((result) => result.items)),
  });

  protected offers(badge: string): ShelfItem[] {
    return this.offerHits.hasValue() ? labelled(this.offerHits.value().hits, badge) : [];
  }

  /** The promotions' names, as the row's small print; empty when they are not known. */
  protected readonly offerRules = computed(() => (this.offerRuleList.hasValue() ? offerRulesOf(this.offerRuleList.value()) : ''));

  protected readonly freeShipping = computed(() =>
    this.offerRuleList.hasValue() ? freeShippingOf(this.offerRuleList.value()) : null,
  );

  // ---------- What rises, what sells, what is new ----------

  private readonly trendingCards = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) =>
      this.sales.trending(RowSize).pipe(
        switchMap((ranked) =>
          ranked.items.length
            ? this.search.cards(params.culture, { ids: rankedIds(ranked) }).pipe(map((cards) => rankedItems(ranked, cards.items)))
            : of([] as ShelfItem[]),
        ),
      ),
  });

  protected trending(rising: string): ShelfItem[] {
    return this.trendingCards.hasValue() ? risingItems(this.trendingCards.value(), rising) : [];
  }

  private readonly bestSellerCards = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) =>
      this.sales.bestSellers(TopSize).pipe(
        switchMap((ranked) =>
          ranked.items.length
            ? this.search.cards(params.culture, { ids: rankedIds(ranked) }).pipe(map((cards) => rankedItems(ranked, cards.items)))
            : of([] as ShelfItem[]),
        ),
      ),
  });

  protected readonly bestSellers = computed<ShelfItem[]>(() =>
    this.bestSellerCards.hasValue() ? this.bestSellerCards.value() : [],
  );

  /**
   * New arrivals, ordered by the date the SOURCE first listed the product — not
   * by when this shop imported it, which is the same second for the whole
   * catalogue and would be sorting by import order while calling it news.
   */
  private readonly newestHits = rxResource({
    params: () => ({ culture: this.culture.active() }),
    stream: ({ params }) => this.search.browse({ culture: params.culture, sort: 'Newest', pageSize: RowSize }),
  });

  protected newest(badge: string): ShelfItem[] {
    return this.newestHits.hasValue() ? labelled(this.newestHits.value().hits, badge) : [];
  }

  // ---------- The departments ----------

  /** The roots of the taxonomy, lower and smaller: the header's row already reaches them in one click. */
  protected readonly departments = computed(() => departmentsOf(this.taxonomy.categories()));

  protected art(code: string): string {
    return departmentArt(code);
  }
}
