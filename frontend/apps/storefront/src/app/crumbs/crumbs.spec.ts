import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { type CrumbLink, Crumbs } from './crumbs';

@Component({
  imports: [Crumbs],
  template: `<storefront-crumbs label="Breadcrumb" [steps]="steps()" [start]="start()" />`,
})
class Host {
  readonly steps = signal<readonly string[]>(['Hogar', 'Cocina', 'Menaje']);
  readonly start = signal<CrumbLink | null>(null);
}

/**
 * Where the shopper stands (ADR 0049): the category page's crumbs, which start
 * at the shop's front, and the product page's, which start at the branch.
 */
describe('the crumbs', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const items = (fixture: ReturnType<typeof render>) =>
    [...(fixture.nativeElement as HTMLElement).querySelectorAll('nav[aria-label="Breadcrumb"] li')].map((li) => li.textContent?.trim());

  it('lists the branch, root first, with a separator between steps and none before the first', () => {
    expect(items(render())).toEqual(['Hogar', '›Cocina', '›Menaje']);
  });

  it('starts with a link when it is given one, and separates the first step from it', () => {
    const fixture = render();
    fixture.componentInstance.start.set({ label: 'Tienda', commands: ['/', 'es'] });
    fixture.detectChanges();

    expect(items(fixture)).toEqual(['Tienda', '›Hogar', '›Cocina', '›Menaje']);
    expect((fixture.nativeElement as HTMLElement).querySelector('li a')?.getAttribute('href')).toBe('/es');
  });

  it('keeps the separators from a screen reader', () => {
    const separators = [...(render().nativeElement as HTMLElement).querySelectorAll('li span')];
    expect(separators.length).toBe(2);
    expect(separators.every((span) => span.getAttribute('aria-hidden') === 'true')).toBe(true);
  });
});
