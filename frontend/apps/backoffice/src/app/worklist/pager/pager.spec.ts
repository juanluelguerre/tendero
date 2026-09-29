import { describe, expect, it } from 'vitest';
import { rangeOf } from './pager';

describe('a page of a list', () => {
  it('counts its rows from one', () => {
    expect(rangeOf(1, 20, 312)).toEqual({ from: 1, to: 20, pages: 16 });
    expect(rangeOf(2, 20, 312)).toEqual({ from: 21, to: 40, pages: 16 });
  });

  it('ends at the total on the last, short page', () => {
    expect(rangeOf(16, 20, 312)).toEqual({ from: 301, to: 312, pages: 16 });
  });

  it('is one empty page when there is nothing', () => {
    expect(rangeOf(1, 20, 0)).toEqual({ from: 0, to: 0, pages: 1 });
  });

  it('shows the last page for a page past the end, as after the list shrank', () => {
    expect(rangeOf(9, 20, 45)).toEqual({ from: 41, to: 45, pages: 3 });
  });
});
