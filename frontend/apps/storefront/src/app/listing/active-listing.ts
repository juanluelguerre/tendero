import { Injectable, signal } from '@angular/core';
import type { ListingState } from './listing-state';

/**
 * The listing on screen, if there is one (R-7.3, F-5).
 *
 * Each page provides its own `ListingState`, so nothing outside the page can
 * inject it; the browser agent's tools are the one thing that needs to. The page's
 * state says here that it is showing and that it has gone, and the tools read
 * it — the same state the column changes, never a second copy of the filters.
 */
@Injectable({ providedIn: 'root' })
export class ActiveListing {
  private readonly state = signal<ListingState | null>(null);

  readonly current = this.state.asReadonly();

  attach(listing: ListingState): void {
    this.state.set(listing);
  }

  /** Only the listing that is current may leave: a new page may already have taken its place. */
  detach(listing: ListingState): void {
    if (this.state() === listing) this.state.set(null);
  }
}
