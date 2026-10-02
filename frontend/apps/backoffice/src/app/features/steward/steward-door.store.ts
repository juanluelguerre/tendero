import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { patchState, signalStore, withMethods, withProps, withState } from '@ngrx/signals';
import { BackofficeUsage } from '../../usage';

/**
 * Whether Steward's panel is open (ADR 0047: the bar's button, the palette and
 * the panel's own close share it, so it is one root store). Apart from the
 * conversation on purpose: the shell needs to know only this, and the
 * conversation's code then travels in the panel's deferred chunk, not in the
 * first bundle every screen loads.
 */
export const StewardDoorStore = signalStore(
  { providedIn: 'root' },
  withState({ isOpen: false }),
  withProps(() => ({ _usage: inject(BackofficeUsage), _router: inject(Router) })),
  withMethods((store) => {
    /** Every opening is counted with the screen it happened on; a closing is not one. */
    const open = () => {
      if (store.isOpen()) return;
      patchState(store, { isOpen: true });
      store._usage.stewardOpened(store._router.url);
    };
    const close = () => patchState(store, { isOpen: false });
    return {
      open,
      close,
      toggle(): void {
        if (store.isOpen()) close();
        else open();
      },
    };
  }),
);
