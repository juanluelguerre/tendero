import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { Icon, type IconName } from './icon';

@Component({
  imports: [Icon],
  template: `<lib-icon [name]="name()" [stroke]="stroke()" />`,
})
class Host {
  readonly name = signal<IconName>('close');
  readonly stroke = signal(1.6);
}

/**
 * Both apps' line icons, one path each (the inventory of 2026-09-30 found the
 * storefront drawing the backoffice's close, menu and chevron inline). An icon
 * never carries meaning alone, so it is hidden from assistive technology: its
 * label is beside it or is its control's name.
 */
describe('an icon', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const svg = (fixture: ReturnType<typeof render>) => (fixture.nativeElement as HTMLElement).querySelector('lib-icon svg') as SVGElement;

  it('draws the path its name gives, and another when the name changes', () => {
    const fixture = render();
    const close = svg(fixture).querySelector('path')?.getAttribute('d');
    fixture.componentInstance.name.set('menu');
    fixture.detectChanges();

    expect([close, svg(fixture).querySelector('path')?.getAttribute('d')]).toEqual(['M6 6l12 12 M18 6L6 18', 'M4 6h16 M4 12h16 M4 18h16']);
  });

  it('is drawn at the weight it is given, in the colour of the text beside it', () => {
    const fixture = render();
    fixture.componentInstance.stroke.set(1.8);
    fixture.detectChanges();
    const path = svg(fixture).querySelector('path') as SVGPathElement;

    expect([path.getAttribute('stroke-width'), path.getAttribute('stroke')]).toEqual(['1.8', 'currentColor']);
  });

  it('is hidden from assistive technology and from the tab order', () => {
    const fixture = render();

    expect([svg(fixture).getAttribute('aria-hidden'), svg(fixture).getAttribute('focusable')]).toEqual(['true', 'false']);
  });
});
