import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { CultureStore } from '@tendero/shared-i18n';
import { ShopFrontService } from '../data-access/shop-front.service';
import { menuOf } from './taxonomy-menu';
import { valueOf } from '@tendero/shared-util';

/**
 * The shop's categories, read once per language and shared by everything that
 * draws them: the menu in the header and the drawer (the redesign's R-5.1), the
 * listing's category facet and the home's departments (ADR 0047).
 */
export const TaxonomyStore = signalStore(
  { providedIn: 'root' },
  withProps(() => {
    const shopFront = inject(ShopFrontService);
    const culture = inject(CultureStore);
    return {
      _list: rxResource({
        params: () => ({ culture: culture.active() }),
        stream: ({ params }) => shopFront.categories(params.culture),
      }),
    };
  }),
  withComputed(({ _list }) => {
    const categories = computed(() => valueOf(_list)?.items ?? []);
    return { categories, menu: computed(() => menuOf(categories())) };
  }),
);
