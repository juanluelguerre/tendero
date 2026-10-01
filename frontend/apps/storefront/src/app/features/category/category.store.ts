import { computed, effect, inject, untracked } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { signalStore, withComputed, withHooks, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { map } from 'rxjs';
import { campaignForCategory, withAncestors } from '../../campaigns/campaign-rules';
import { CampaignsStore } from '../../campaigns/campaigns.store';
import { ShopFrontService, type Category } from '../../data-access/shop-front.service';
import { ListingStore } from '../../listing/listing.store';
import { childLinks } from '../../listing/listing-view';
import { departmentArt, rootOf } from '../../shop-art';
import { extendResource } from '@ngrx/signals/resource';

/** The taxonomy's answer about the code in the URL; the products are the listing's. */
export type CategoryPageState = 'loading' | 'found' | 'unknown' | 'failed';

/**
 * A category page's own state (ADR 0047): the branch the URL names, its
 * sections, its department's drawing and its campaign — and, once the branch is
 * known, what the page's listing lists. The products are the listing's store,
 * provided by the same page; this tells it where.
 *
 * **The taxonomy first**, because a code nobody recognises is a 404 rather than
 * an empty grid — "no products here" and "no such category" are different
 * answers and only one of them is the shopper's fault. It is a resource, so a
 * change of code or language cancels the question in flight rather than racing
 * it.
 */
export const CategoryStore = signalStore(
  withProps(() => {
    const route = inject(ActivatedRoute);
    const shopFront = inject(ShopFrontService);
    const culture = inject(CultureStore);
    const code = toSignal(route.paramMap.pipe(map((params) => params.get('code') ?? '')), { initialValue: '' });
    return {
      _campaigns: inject(CampaignsStore),
      _listing: inject(ListingStore),
      _code: code,
      _taxonomy: extendResource(
        rxResource({
          params: () => (code() ? { culture: culture.active() } : undefined),
          stream: ({ params }) => shopFront.categories(params.culture).pipe(map((list) => list.items)),
        }),
      ),
    };
  }),
  withComputed(({ _code, _taxonomy, _campaigns, _listing }) => {
    /** The whole taxonomy as last answered, to walk a category up to its department. */
    const tree = computed<readonly Category[]>(() => (_taxonomy.hasValue() ? _taxonomy.value() : []));
    /** The category itself, for the heading and the breadcrumb. */
    const category = computed(() => tree().find((item) => item.code.toLowerCase() === _code().toLowerCase()) ?? null);
    /** Its children, so a department offers the sections inside it. */
    const children = computed(() => {
      const found = category();
      return found ? tree().filter((item) => item.parent === found.code) : [];
    });
    return {
      state: computed<CategoryPageState>(() => {
        if (_taxonomy.status() === 'error') return 'failed';
        if (!_taxonomy.hasValue() || _taxonomy.isLoading()) return 'loading';
        return category() ? 'found' : 'unknown';
      }),
      category,
      children,
      /**
       * The department's illustration, borrowed by everything under it.
       *
       * `COFFEE_MAKER` does not get a drawing of its own — it gets Hogar's,
       * because that is the thing a shopper recognises and because art per leaf
       * is twenty-three drawings to keep in step with a taxonomy that changes.
       */
      art: computed(() => {
        const found = category();
        return found ? departmentArt(rootOf(found.code, tree())) : null;
      }),
      /**
       * The campaign of this branch, as the banner at the head of its pages
       * (R-8): *Semana del hogar* on Hogar and on everything under it. A
       * category's campaign never reaches the home; this is where it applies.
       */
      banner: computed(() => {
        const found = category();
        return found ? campaignForCategory(_campaigns.all(), withAncestors(found.code, tree())) : null;
      }),
      /** The sections again, as the column's links, with the counts the listing's category facet gives them. */
      navigation: computed(() => {
        const found = category();
        const result = _listing.result();
        return found ? childLinks(found.code, children(), result.status === 'ready' ? result.facets : []) : null;
      }),
    };
  }),
  withHooks({
    /** The branch, once known, is what the listing lists; a code nobody recognises lists nothing. */
    onInit(store) {
      effect(() => {
        const state = store.state();
        const found = store.category();
        untracked(() => {
          if (state === 'found' && found) store._listing.list({ query: '', category: found.code });
          else if (state === 'unknown') store._listing.list(null);
        });
      });
    },
  }),
);
