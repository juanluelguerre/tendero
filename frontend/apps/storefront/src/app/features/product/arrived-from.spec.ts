import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { arrivedFrom } from './arrived-from';

describe('arrivedFrom, the kind of page that led to a product', () => {
  const tree = (url: string) => TestBed.inject(Router).parseUrl(url);

  it.each([
    ['/es?q=mochila', 'search'],
    ['/en', 'home'],
    ['/es/c/KITCHEN', 'category'],
    ['/en/cart', 'other'],
    ['/es/p/camisa-de-lino/K7M2QX9P4T', 'other'],
  ])('reads %s as %s', (url, expected) => {
    expect(arrivedFrom(tree(url))).toBe(expected);
  });

  it('is direct when nothing came before it in this tab', () => {
    expect(arrivedFrom(null)).toBe('direct');
  });
});
