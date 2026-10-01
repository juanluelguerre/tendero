import { computed, inject } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { signalStore, withComputed, withProps } from '@ngrx/signals';
import { PricingService } from '../../data-access/pricing.service';
import { readStatus } from '../../read-status';
import { extendResource } from '@ngrx/signals/resource';

/** The promotions' state (ADR 0047): the list, read once, and how many are running. */
export const PromotionsStore = signalStore(
  withProps(() => {
    const pricing = inject(PricingService);
    const promotions = extendResource(rxResource({ stream: () => pricing.promotions() }));
    return { _promotions: promotions, ...readStatus(promotions) };
  }),
  withComputed((store) => {
    const items = computed(() => (store._promotions.hasValue() ? store._promotions.value().items : []));
    return {
      items,
      running: computed(() => items().filter((promotion) => promotion.isActive).length),
    };
  }),
);
