import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Summary } from './summary';

@Component({
  imports: [Summary],
  template: `<div class="grid">
    <storefront-summary><p>Total 99,95 €</p></storefront-summary>
  </div>`,
})
class Host {}

/**
 * The panel an order's figures sit in, beside the lines, on the cart, the
 * checkout and the order: one aside, so the three cannot drift apart.
 */
describe('the summary', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('holds what it is given in a complementary region', () => {
    expect(render().querySelector('aside p')?.textContent).toBe('Total 99,95 €');
  });

  it('is the grid’s own child, so it can stick beside the lines', () => {
    const host = render().querySelector('storefront-summary') as HTMLElement;

    expect([host.classList.contains('contents'), host.querySelector('aside')?.classList.contains('sticky')]).toEqual([
      true,
      true,
    ]);
  });
});
