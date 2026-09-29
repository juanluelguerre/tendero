import { patchState, signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';

/** How long an offer to undo stays: long enough to read and reach, short enough not to linger. */
const OfferFor = 10_000;

export interface UndoOffer {
  readonly message: string;
  readonly undo: () => void;
}

/**
 * Undo instead of "are you sure?" (R-2, P12), and only where the domain has the
 * reverse and nothing has left the shop: suspending an agent offers reinstating
 * it, withdrawing a product offers publishing it. Undoing is the reverse
 * operation on the server, audited like any other — never a local rollback.
 *
 * One store for the whole app (ADR 0047): the screens offer, the shell's toast
 * shows the offer and takes it.
 */
export const UndoStore = signalStore(
  { providedIn: 'root' },
  withState<{ offer: UndoOffer | null }>({ offer: null }),
  withProps(() => ({ _timer: { handle: undefined as ReturnType<typeof setTimeout> | undefined } })),
  withMethods((store) => {
    const stop = () => clearTimeout(store._timer.handle);
    const dismiss = () => {
      stop();
      patchState(store, { offer: null });
    };

    return {
      offerTo(message: string, undo: () => void): void {
        stop();
        patchState(store, { offer: { message, undo } });
        store._timer.handle = setTimeout(() => patchState(store, { offer: null }), OfferFor);
      },
      take(): void {
        const offer = store.offer();
        dismiss();
        offer?.undo();
      },
      dismiss,
    };
  }),
  withHooks({
    onDestroy(store) {
      clearTimeout(store._timer.handle);
    },
  }),
);
