import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { map, of, switchMap } from 'rxjs';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ProductSearchService } from '../../data-access/product-search.service';
import { ShopFrontService } from '../../data-access/shop-front.service';
import { WhatSellsService } from '../../data-access/what-sells.service';
import type { ShelfItem } from '../../shelves/product-row/product-row';
import { TaxonomyStore } from '../../taxonomy/taxonomy.store';
import { TrailStore } from '../../trail/trail.store';
import { departmentsOf, freeShippingOf, offerRulesOf, rankedIds, rankedItems } from './home-bands';
import { extendResource } from '@ngrx/signals/resource';

/** How many cards a row asks for: two screens of a desktop row, one of them whole. */
const RowSize = 10;
/** *Lo más vendido* is a top five. */
const TopSize = 5;

/**
 * What the front of the shop reads (R-8, docs/specs/storefront-home.md; ADR
 * 0047): the leading campaign's products, the trail's cards and what they
 * suggest, the offers and their rules, what rises, what sells, what is new, and
 * the departments.
 *
 * **Each band fails alone.** Every one is its own request, as a resource, and a
 * band whose request failed or answered nothing reads as empty — the page does
 * not draw it. A home that renders nothing because the rankings are down is a
 * shop that closes over a chart. The words each band wears are the page's.
 */
export const HomeStore = signalStore(
  withProps(() => {
    const search = inject(ProductSearchService);
    const sales = inject(WhatSellsService);
    const shopFront = inject(ShopFrontService);
    const culture = inject(CultureStore);
    const campaigns = inject(CampaignsStore);
    const trail = inject(TrailStore);

    const viewedCodes = computed(() => trail.shown().views.map((entry) => entry.key));
    const byCulture = () => ({ culture: culture.active() });
    const withViews = () => (viewedCodes().length ? { culture: culture.active(), codes: viewedCodes() } : undefined);
    const rankedCards = (ranking: typeof sales.trending, size: number) =>
      extendResource(
        rxResource({
          params: byCulture,
          stream: ({ params }) =>
            ranking(size).pipe(
              switchMap((ranked) =>
                ranked.items.length
                  ? search
                      .cards(params.culture, { ids: rankedIds(ranked) })
                      .pipe(map((cards) => rankedItems(ranked, cards.items)))
                  : of([] as ShelfItem[]),
              ),
            ),
        }),
      );

    return {
      _taxonomy: inject(TaxonomyStore),
      /** The campaign the home leads with, or null. */
      campaign: campaigns.leading,
      /** What the leading campaign reaches, for its card when it has no picture of its own. */
      _campaignProducts: extendResource(
        rxResource({
          params: () => {
            const campaign = campaigns.leading();
            return campaign && !campaign.imageId && campaign.phase === 'Running'
              ? { culture: culture.active(), campaign: campaign.code }
              : undefined;
          },
          stream: ({ params }) => search.browse({ ...params, pageSize: 3 }).pipe(map((page) => page.hits)),
        }),
      ),
      _viewedCards: extendResource(
        rxResource({
          params: withViews,
          stream: ({ params }) =>
            search.cards(params.culture, { codes: params.codes }).pipe(map((result) => result.items)),
        }),
      ),
      _similar: extendResource(
        rxResource({
          params: withViews,
          stream: ({ params }) => search.similar(params.culture, params.codes, RowSize),
        }),
      ),
      _offerHits: extendResource(
        rxResource({
          params: byCulture,
          stream: ({ params }) => search.browse({ culture: params.culture, onOffer: true, pageSize: RowSize }),
        }),
      ),
      _offerRules: extendResource(
        rxResource({
          params: byCulture,
          stream: ({ params }) => shopFront.offers(params.culture).pipe(map((result) => result.items)),
        }),
      ),
      _trending: rankedCards((size) => sales.trending(size), RowSize),
      _bestSellers: rankedCards((size) => sales.bestSellers(size), TopSize),
      /**
       * New arrivals, ordered by the date the SOURCE first listed the product —
       * not by when this shop imported it, which is the same second for the
       * whole catalogue and would be sorting by import order while calling it
       * news.
       */
      _newest: extendResource(
        rxResource({
          params: byCulture,
          stream: ({ params }) => search.browse({ culture: params.culture, sort: 'Newest', pageSize: RowSize }),
        }),
      ),
    };
  }),
  withComputed((store) => ({
    campaignHits: computed<readonly SearchHit[]>(() =>
      store._campaignProducts.hasValue() ? store._campaignProducts.value() : [],
    ),
    /** *Sigue donde lo dejaste*: the trail's products, as cards. */
    viewed: computed<readonly SearchHit[]>(() => (store._viewedCards.hasValue() ? store._viewedCards.value() : [])),
    /** What the trail suggests, before the page words it; null when it is not known. */
    similar: computed(() => (store._similar.hasValue() ? store._similar.value() : null)),
    offerHits: computed<readonly SearchHit[]>(() => (store._offerHits.hasValue() ? store._offerHits.value().hits : [])),
    /** The promotions' names, as the row's small print; empty when they are not known. */
    offerRules: computed(() => (store._offerRules.hasValue() ? offerRulesOf(store._offerRules.value()) : '')),
    freeShipping: computed(() => (store._offerRules.hasValue() ? freeShippingOf(store._offerRules.value()) : null)),
    trendingItems: computed<ShelfItem[]>(() => (store._trending.hasValue() ? store._trending.value() : [])),
    bestSellers: computed<ShelfItem[]>(() => (store._bestSellers.hasValue() ? store._bestSellers.value() : [])),
    newestHits: computed<readonly SearchHit[]>(() => (store._newest.hasValue() ? store._newest.value().hits : [])),
    /** The roots of the taxonomy, lower and smaller: the header's row already reaches them in one click. */
    departments: computed(() => departmentsOf(store._taxonomy.categories())),
  })),
);
