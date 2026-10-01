import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import type { SearchHit } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { map } from 'rxjs';
import { ProductSearchService } from '../../data-access/product-search.service';
import { hasTrail } from '../trail-rules';
import { TrailStore } from '../trail.store';
import { extendResource } from '@ngrx/signals/resource';

/**
 * *Tu historial*'s own state (R-8, H-3b; ADR 0047): the whole trail as the
 * shop's one trail keeps it, and the products it names read as cards. The
 * switch and the removals are the trail's.
 */
export const TrailHistoryStore = signalStore(
  withProps(() => {
    const trail = inject(TrailStore);
    const search = inject(ProductSearchService);
    const culture = inject(CultureStore);
    const codes = computed(() => trail.kept().views.map((entry) => entry.key));
    return {
      kept: trail.kept,
      _cards: extendResource(
        rxResource({
          params: () => (codes().length ? { culture: culture.active(), codes: codes() } : undefined),
          stream: ({ params }) =>
            search.cards(params.culture, { codes: params.codes }).pipe(map((result) => result.items)),
        }),
      ),
    };
  }),
  withComputed(({ kept, _cards }) => ({
    any: computed(() => hasTrail(kept())),
    viewed: computed<readonly SearchHit[]>(() => (_cards.hasValue() ? _cards.value() : [])),
  })),
);
