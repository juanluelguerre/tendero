import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { UndoStore } from './undo.store';

/** Undo instead of "are you sure?" (R-2, P12), as one store for the whole app (ADR 0047): screens offer, the toast shows. */
describe('the undo store', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('keeps an offer for ten seconds, then lets it go', () => {
    const store = TestBed.inject(UndoStore);
    store.offerTo('Product withdrawn', () => undefined);
    vi.advanceTimersByTime(9_999);
    const stillThere = store.offer()?.message;
    vi.advanceTimersByTime(1);

    expect([stillThere, store.offer()]).toEqual(['Product withdrawn', null]);
  });

  it('runs the reverse once when taken, and the offer is gone', () => {
    const store = TestBed.inject(UndoStore);
    const reverse = vi.fn();
    store.offerTo('Agent suspended', reverse);
    store.take();
    store.take();

    expect([reverse.mock.calls.length, store.offer()]).toEqual([1, null]);
  });

  /** A second action's offer replaces the first, with its own ten seconds. */
  it('replaces an offer with the next one, and its clock with it', () => {
    const store = TestBed.inject(UndoStore);
    store.offerTo('First', () => undefined);
    vi.advanceTimersByTime(6_000);
    store.offerTo('Second', () => undefined);
    vi.advanceTimersByTime(6_000);

    expect(store.offer()?.message).toBe('Second');
  });
});
