import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { ProductImage } from './product-image';

@Component({
  imports: [ProductImage],
  template: `<storefront-product-image
    [src]="src()"
    alt="Mochila"
    [labelled]="labelled()"
    [priority]="priority()"
    (failed)="failures = failures + 1"
  />`,
})
class Host {
  readonly src = signal<string | null>('/api/images/a');
  readonly labelled = signal(false);
  readonly priority = signal(false);
  failures = 0;
}

/**
 * A product's picture, or the placeholder (ADR 0049): the seed points at images
 * that may not be there, so a failure is the default path and not an error.
 */
describe('a product image', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: { product: { noImage: 'No image yet' } } },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
    }),
  );

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const element = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;
  const placeholder = (fixture: ReturnType<typeof render>) =>
    element(fixture).querySelector('storefront-product-image-placeholder');

  /**
   * **A picture's priority changes, and the page goes on drawing.** A listing
   * gives it to its first row; narrow the listing and a product that was in the
   * third row is in the first. NgOptimizedImage refuses a `priority` changed
   * after it started — it throws, in development — and the throw cut the whole
   * page's rendering short: the filter column beside the listing kept the
   * counts of the list before (found by the storefront's browser spec,
   * 2026-10-08). A picture whose priority changes is another picture element.
   */
  it('takes a new priority without failing, and loads the picture as that priority asks', () => {
    const fixture = render();
    const loading = () => element(fixture).querySelector('img')?.getAttribute('loading');
    const before = loading();

    fixture.componentInstance.priority.set(true);
    expect(() => fixture.detectChanges()).not.toThrow();
    const first = loading();
    fixture.componentInstance.priority.set(false);
    expect(() => fixture.detectChanges()).not.toThrow();

    expect([before, first, loading()]).toEqual(['lazy', 'eager', 'lazy']);
    expect(element(fixture).querySelector('img')?.getAttribute('src')).toBe('/api/images/a');
  });

  it('shows the picture with its words', () => {
    const fixture = render();
    const image = element(fixture).querySelector('img');
    expect(image?.getAttribute('src')).toBe('/api/images/a');
    expect(image?.alt).toBe('Mochila');
    expect(placeholder(fixture)).toBeNull();
  });

  it('draws the placeholder when there is no picture, with the words only when asked', () => {
    const fixture = render();
    fixture.componentInstance.src.set(null);
    fixture.detectChanges();
    expect(element(fixture).querySelector('img')).toBeNull();
    expect(placeholder(fixture)?.getAttribute('aria-label')).toBe('No image yet');
    expect(placeholder(fixture)?.textContent?.trim()).toBe('');

    fixture.componentInstance.labelled.set(true);
    fixture.detectChanges();
    expect(placeholder(fixture)?.textContent?.trim()).toBe('No image yet');
  });

  it('swaps in the placeholder when the picture fails, says so once, and tries a new address afresh', () => {
    const fixture = render();
    element(fixture).querySelector('img')?.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(element(fixture).querySelector('img')).toBeNull();
    expect(placeholder(fixture)).not.toBeNull();
    expect(fixture.componentInstance.failures).toBe(1);

    fixture.componentInstance.src.set('/api/images/b');
    fixture.detectChanges();
    expect(element(fixture).querySelector('img')?.getAttribute('src')).toBe('/api/images/b');
  });
});
