import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { outcomeOf, viewOf } from './usage';

/** R-0's backoffice measurement: a screen by its route, an outcome by the server's answer. */
describe('which screen a URL is', () => {
  it.each([
    ['/dashboard', 'dashboard'],
    ['/', 'dashboard'],
    ['/orders?status=Confirmed', 'orders'],
    ['/products/0199a1f0-0000/variants', 'variants'],
    ['/agents#top', 'agents'],
  ])('reads %s as %s', (url, view) => {
    expect(viewOf(url)).toBe(view);
  });

  it('does not measure what is not a shopkeeper’s screen', () => {
    expect(viewOf('/sign-in?returnTo=/dashboard')).toBeNull();
    expect(viewOf('/products/0199a1f0-0000')).toBeNull();
  });
});

describe('how the server answered', () => {
  it('is refused for a 4xx and failed for anything else', () => {
    expect(outcomeOf(new HttpErrorResponse({ status: 409 }))).toBe('refused');
    expect(outcomeOf(new HttpErrorResponse({ status: 400 }))).toBe('refused');
    expect(outcomeOf(new HttpErrorResponse({ status: 503 }))).toBe('failed');
    expect(outcomeOf(new HttpErrorResponse({ status: 0 }))).toBe('failed');
    expect(outcomeOf(new Error('boom'))).toBe('failed');
  });
});
