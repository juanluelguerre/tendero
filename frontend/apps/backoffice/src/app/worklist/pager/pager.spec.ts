import { describe, expect, it } from 'vitest';
import { rangeOf } from './pager';

describe('a page of a list', () => {
  it('counts its rows from one', () => {
    expect(rangeOf(1, 20, 312)).toEqual({ from: 1, to: 20, pages: 16, current: 1 });
    expect(rangeOf(2, 20, 312)).toEqual({ from: 21, to: 40, pages: 16, current: 2 });
  });

  it('ends at the total on the last, short page', () => {
    expect(rangeOf(16, 20, 312)).toEqual({ from: 301, to: 312, pages: 16, current: 16 });
  });

  it('is one empty page when there is nothing', () => {
    expect(rangeOf(1, 20, 0)).toEqual({ from: 0, to: 0, pages: 1, current: 1 });
  });

  /**
   * The URL can name a page the list no longer has. The pager shows the last,
   * and moves from it: previous from "page 9" of three is page 2, not page 8 —
   * which every list now gets, where only the catalogue's store used to clamp.
   */
  it('shows the last page for a page past the end, as after the list shrank, and moves from it', () => {
    expect(rangeOf(9, 20, 45)).toEqual({ from: 41, to: 45, pages: 3, current: 3 });
  });
});
