import { signal } from '@angular/core';
import { signalStore, withMethods, withProps } from '@ngrx/signals';
import type { ListingStore } from './listing.store';

type Listing = InstanceType<typeof ListingStore>;

/**
 * The listing on screen, if there is one (R-7.3, F-5; ADR 0047).
 *
 * Each page provides its own `ListingStore`, so nothing outside the page can
 * inject it; the browser agent's tools are the one thing that needs to. The
 * page's store says here that it is showing and that it has gone, and the tools
 * read it — the same store the column changes, never a second copy of the
 * filters.
 *
 * The listing is held in a signal of its own and not in the store's state: a
 * store's state is frozen, and a listing is a live object, not a value.
 */
export const ActiveListing = signalStore(
  { providedIn: 'root' },
  withProps(() => {
    const listing = signal<Listing | null>(null);
    return { _listing: listing, current: listing.asReadonly() };
  }),
  withMethods((store) => ({
    attach(listing: Listing): void {
      store._listing.set(listing);
    },
    /** Only the listing that is current may leave: a new page may already have taken its place. */
    detach(listing: Listing): void {
      if (store._listing() === listing) store._listing.set(null);
    },
  })),
);
