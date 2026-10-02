import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';

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
  withMethods((store) => ({
    open(): void {
      patchState(store, { isOpen: true });
    },
    close(): void {
      patchState(store, { isOpen: false });
    },
    toggle(): void {
      patchState(store, (state) => ({ isOpen: !state.isOpen }));
    },
  })),
);
