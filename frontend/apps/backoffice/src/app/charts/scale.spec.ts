import { describe, expect, it } from 'vitest';
import { niceMax, ticks } from './scale';

describe('a chart scale a person reads', () => {
  it('rounds the tallest bar up to a number with one significant step', () => {
    expect(niceMax(930)).toBe(1000);
    expect(niceMax(612)).toBe(1000);
    expect(niceMax(480)).toBe(500);
    expect(niceMax(210)).toBe(250);
    expect(niceMax(12)).toBe(20);
    expect(niceMax(1)).toBe(1);
  });

  it('keeps an empty chart drawable', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(-5)).toBe(1);
  });

  it('marks the axis in equal steps from zero', () => {
    expect(ticks(1000)).toEqual([0, 250, 500, 750, 1000]);
    expect(ticks(20, 2)).toEqual([0, 10, 20]);
  });
});
