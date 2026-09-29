import { inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { CartStore } from '../../cart/cart.store';
import { OrderService } from '../../data-access/order.service';

/** What the hand-off is doing: taking the cart over, or why it could not. */
export type ContinueCheckoutState = 'taking-over' | 'gone' | 'failed';

/**
 * The hand-off of an agent's checkout to its person (UCP's
 * requires_escalation, P11-1; ADR 0047): the cart the agent chose becomes this
 * browser's, and checkout opens in the language the agent shopped in.
 */
export const ContinueCheckoutStore = signalStore(
  withState<{ state: ContinueCheckoutState }>({ state: 'taking-over' }),
  withProps(() => ({
    _route: inject(ActivatedRoute),
    _orders: inject(OrderService),
    _cart: inject(CartStore),
    _router: inject(Router),
  })),
  withMethods((store) => ({
    async takeOver(): Promise<void> {
      const sessionId = store._route.snapshot.paramMap.get('sessionId') ?? '';
      try {
        const handOff = await store._orders.handOff(sessionId);
        await store._cart.adopt(handOff.cartToken);
        // In the language the agent checked out in: the basket's names and the
        // order's language follow it.
        await store._router.navigate(['/', handOff.culture, 'checkout'], { replaceUrl: true });
      } catch (error) {
        patchState(store, { state: (error as { status?: number })?.status === 404 ? 'gone' : 'failed' });
      }
    },
  })),
);
