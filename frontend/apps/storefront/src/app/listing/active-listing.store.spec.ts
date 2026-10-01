import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ActiveListing } from './active-listing.store';
import type { ListingStore } from './listing.store';

type Listing = InstanceType<typeof ListingStore>;

/** The listing on screen, for the browser agent's tools: the one a page attached, until it leaves. */
describe('the active listing', () => {
  const listing = () => ({}) as Listing;

  it('is the listing a page attached', () => {
    const active = TestBed.inject(ActiveListing);
    const shown = listing();
    active.attach(shown);

    expect(active.current()).toBe(shown);
  });

  it('is nothing once that listing leaves', () => {
    const active = TestBed.inject(ActiveListing);
    const shown = listing();
    active.attach(shown);
    active.detach(shown);

    expect(active.current()).toBeNull();
  });

  /** Navigating attaches the new page before the old one is destroyed. */
  it('stays the new page’s when the old page leaves after it', () => {
    const active = TestBed.inject(ActiveListing);
    const before = listing();
    const after = listing();
    active.attach(before);
    active.attach(after);
    active.detach(before);

    expect(active.current()).toBe(after);
  });
});
