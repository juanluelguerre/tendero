import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { ProductImageView } from '@tendero/shared-api';
import { API_BASE_URL } from '@tendero/shared-util';
import { beforeEach, describe, expect, it } from 'vitest';
import { ProductGallery } from './product-gallery';

const picture = (imageId: string) => ({ imageId, alt: null }) as unknown as ProductImageView;

@Component({
  imports: [ProductGallery],
  template: `<storefront-product-gallery
    [images]="images()"
    [current]="current()"
    [position]="position()"
    name="Alforja"
    (show)="said.push('show ' + $event)"
    (step)="said.push('step ' + $event)"
  />`,
})
class Host {
  readonly images = signal([picture('cover'), picture('black'), picture('detail'), picture('back')]);
  readonly current = signal<ProductImageView | undefined>(picture('detail'));
  readonly position = signal({ index: 3, total: 4 });
  readonly said: string[] = [];
}

/**
 * The product page's pictures (the gallery's mock-up, 2026-10-15): the one in
 * front, arrows either side of it, a count, and a row of thumbnails that never
 * moves. It shows and it asks; the page's store decides which picture is next.
 */
describe('the product gallery', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              product: { noImage: 'No image yet', gallery: { previous: 'Previous picture', next: 'Next picture' } },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
        }),
      ],
      providers: [{ provide: API_BASE_URL, useValue: '' }],
    }),
  );

  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture;
  }
  const element = (fixture: ReturnType<typeof render>) => fixture.nativeElement as HTMLElement;
  const said = (fixture: ReturnType<typeof render>) => fixture.componentInstance.said;

  it('puts the current picture in front, says where it is, and marks its thumbnail', () => {
    const page = element(render());

    expect([
      page.querySelector('[data-stage] img')?.getAttribute('src'),
      page.querySelector('[data-count]')?.textContent?.replace(/\s+/g, ' ').trim(),
      [...page.querySelectorAll('[data-thumbs] button')].map((b) => b.getAttribute('aria-current')),
    ]).toEqual(['/api/images/detail', '3 / 4', [null, null, 'true', null]]);
  });

  it('asks for the previous and the next picture with its arrows, named for a screen reader', () => {
    const fixture = render();
    const page = element(fixture);
    page.querySelector<HTMLButtonElement>('button[aria-label="Previous picture"]')?.click();
    page.querySelector<HTMLButtonElement>('button[aria-label="Next picture"]')?.click();

    expect(said(fixture)).toEqual(['step -1', 'step 1']);
  });

  it('asks for a thumbnail pressed, and steps with the arrow keys', () => {
    const fixture = render();
    const page = element(fixture);
    page.querySelectorAll<HTMLButtonElement>('[data-thumbs] button')[1].click();
    page
      .querySelector('[data-stage]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    page
      .querySelector('[data-stage]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft', bubbles: true }));

    expect(said(fixture)).toEqual(['show black', 'step 1', 'step -1']);
  });

  /** A finger: a swipe to the left is the next picture, as on every phone; a tap is no swipe. */
  it('steps with a swipe, and not with a tap', () => {
    const fixture = render();
    const stage = element(fixture).querySelector('[data-stage]') as HTMLElement;
    const swipe = (from: number, to: number) => {
      stage.dispatchEvent(new PointerEvent('pointerdown', { clientX: from, bubbles: true }));
      stage.dispatchEvent(new PointerEvent('pointerup', { clientX: to, bubbles: true }));
    };
    swipe(300, 180);
    swipe(180, 300);
    swipe(200, 205);

    expect(said(fixture)).toEqual(['step 1', 'step -1']);
  });

  it('has no arrows, no count and no thumbnails with a single picture', () => {
    const fixture = render();
    fixture.componentInstance.images.set([picture('cover')]);
    fixture.componentInstance.current.set(picture('cover'));
    fixture.componentInstance.position.set({ index: 1, total: 1 });
    fixture.detectChanges();
    const page = element(fixture);

    expect([
      page.querySelectorAll('[data-stage] button').length,
      page.querySelector('[data-count]'),
      page.querySelector('[data-thumbs]'),
    ]).toEqual([0, null, null]);
  });
});
