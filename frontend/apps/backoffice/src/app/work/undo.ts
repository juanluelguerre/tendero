import { DestroyRef, inject, Injectable, signal } from '@angular/core';

/** How long an offer to undo stays: long enough to read and reach, short enough not to linger. */
const OfferFor = 10_000;

interface UndoOffer {
  readonly message: string;
  readonly undo: () => void;
}

/**
 * Undo instead of "are you sure?" (R-2, P12), and only where the domain has the
 * reverse and nothing has left the shop: suspending an agent offers reinstating
 * it, withdrawing a product offers publishing it. Undoing is the reverse
 * operation on the server, audited like any other — never a local rollback.
 */
@Injectable({ providedIn: 'root' })
export class Undo {
  readonly offer = signal<UndoOffer | null>(null);
  private timer?: ReturnType<typeof setTimeout>;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  offerTo(message: string, undo: () => void): void {
    clearTimeout(this.timer);
    this.offer.set({ message, undo });
    this.timer = setTimeout(() => this.offer.set(null), OfferFor);
  }

  take(): void {
    const offer = this.offer();
    this.dismiss();
    offer?.undo();
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.offer.set(null);
  }
}
