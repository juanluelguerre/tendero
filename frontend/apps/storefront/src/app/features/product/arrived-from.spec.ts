import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import type { Navigation } from '@angular/router';
import { arrivedFrom, arrivedVia, cameFiltered } from './arrived-from';

describe('arrivedFrom, the kind of page that led to a product', () => {
  const tree = (url: string) => TestBed.inject(Router).parseUrl(url);

  it.each([
    ['/es?q=mochila', 'search'],
    ['/en', 'home'],
    ['/es?offer=1', 'listing'],
    ['/es?sort=newest', 'home'],
    ['/es/c/KITCHEN', 'category'],
    ['/en/cart', 'other'],
    ['/es/p/camisa-de-lino/K7M2QX9P4T', 'other'],
  ])('reads %s as %s', (url, expected) => {
    expect(arrivedFrom(tree(url))).toBe(expected);
  });

  it('is direct when nothing came before it in this tab', () => {
    expect(arrivedFrom(null)).toBe('direct');
  });

  it.each([
    ['/es/c/SHOES?brand=Vertex', true],
    ['/es?q=mochila&f=COLOR:BLACK', true],
    ['/es?offer=1', true],
    ['/es/c/SHOES?sort=price-asc', false],
    ['/es/c/SHOES', false],
    ['/es/cart?brand=Vertex', false],
  ])('reads %s as filtered: %s', (url, expected) => {
    expect(cameFiltered(tree(url))).toBe(expected);
  });

  it('is not filtered when nothing came before', () => {
    expect(cameFiltered(null)).toBe(false);
  });

  describe('from a row of the home page', () => {
    const navigation = (state: Record<string, unknown> | undefined, previous: string | null): Navigation =>
      ({
        extras: { state },
        previousNavigation: previous ? { finalUrl: tree(previous) } : null,
      }) as unknown as Navigation;

    it.each(['trail', 'campaign', 'suggested', 'trending'] as const)('reads a link that says %s', (via) => {
      expect(arrivedVia(navigation({ via }, '/es'))).toBe(via);
    });

    it('falls back to the page before when the link says nothing, or something unknown', () => {
      expect(arrivedVia(navigation(undefined, '/es?q=mochila'))).toBe('search');
      expect(arrivedVia(navigation({ via: 'banner' }, '/es'))).toBe('home');
      expect(arrivedVia(null)).toBe('direct');
    });
  });
});
