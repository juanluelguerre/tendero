import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import { beforeEach, describe, expect, it } from 'vitest';
import { ProductImage } from './product-image';

@Component({
  imports: [ProductImage],
  template: `<storefront-product-image [src]="src()" alt="Mochila" [labelled]="labelled()" (failed)="failures = failures + 1" />`,
})
class Host {
  readonly src = signal<string | null>('/api/images/a');
  readonly labelled = signal(false);
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
  const placeholder = (fixture: ReturnType<typeof render>) => element(fixture).querySelector('storefront-product-image-placeholder');

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
