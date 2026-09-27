import { TestBed } from '@angular/core/testing';
import { Router, type Navigation } from '@angular/router';
import { openedVia } from './opened-via';

/**
 * How a shopper reached a listing (R-0's `category_opened`): the menu when its link
 * says so, the kind of page before otherwise — so the baseline, taken before the
 * menu exists, and the after, taken with it, are the same measurement.
 */
describe('openedVia, how a category listing was reached', () => {
  const navigation = (previous: string | null, state?: Record<string, unknown>) =>
    ({
      extras: { state },
      previousNavigation: previous ? { finalUrl: TestBed.inject(Router).parseUrl(previous) } : null,
    }) as unknown as Navigation;

  it('is the menu when the menu’s link says so, wherever the shopper was', () => {
    expect(openedVia(navigation('/es/p/camisa/K7M2QX9P4T', { via: 'menu' }))).toBe('menu');
  });

  it.each([
    ['/es?q=mochila', 'search'],
    ['/en', 'home'],
    ['/es/c/HOME', 'category'],
    ['/es/p/camisa/K7M2QX9P4T', 'other'],
  ])('otherwise reads the page before, %s, as %s', (previous, expected) => {
    expect(openedVia(navigation(previous))).toBe(expected);
  });

  it('is direct with nothing before it', () => {
    expect(openedVia(navigation(null))).toBe('direct');
    expect(openedVia(null)).toBe('direct');
  });
});
