import { Component, input, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@jsverse/transloco';
import type { SearchHit, VisualMatch } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ProductCard } from '../../product-card/product-card';
import { VisualSearchStore } from '../../visual-search/visual-search.store';
import { VisualSearchPage } from './visual-search-page';

/** The card, standing in: what the page hands it, and a link to follow. */
@Component({
  selector: 'storefront-product-card',
  template: `<a href="#" (click)="$event.preventDefault()" [attr.data-image]="hit().imageId">{{ hit().code }}</a>`,
})
class CardStub {
  readonly hit = input.required<SearchHit>();
  readonly priority = input(false);
}

const words = {
  visualSearch: {
    title: 'Search with a picture',
    nearest: 'The nearest things we have',
    nearestLede: 'We may not sell the thing itself.',
    yourPicture: 'Your picture',
    another: 'Try another picture',
    byWords: 'Search by words instead',
    nothing: 'No picture in the shop looks like yours.',
    noPicture: 'Give the shop a picture.',
  },
};

const match = (code: string, picture: string): VisualMatch =>
  ({
    product: { productId: `id-${code}`, code, imageId: `cover-${code}` } as SearchHit,
    picture,
    similarity: 0.8,
  }) as VisualMatch;

/** The answer to a picture: what it promises, what each card shows, and what a page with no picture says. */
describe('the page of the nearest things', () => {
  let fixture: ComponentFixture<VisualSearchPage>;
  const matches = signal<readonly VisualMatch[] | null>(null);
  const preview = signal<string | null>(null);
  const store = { matches, preview, open: vi.fn(), followed: vi.fn() };

  beforeEach(async () => {
    matches.set(null);
    preview.set(null);
    store.open.mockClear();
    store.followed.mockClear();
    TestBed.configureTestingModule({
      imports: [
        TranslocoTestingModule.forRoot({
          langs: { en: words },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
      ],
      providers: [
        provideRouter([]),
        { provide: VisualSearchStore, useValue: store },
        { provide: CultureStore, useValue: { active: signal('en') } },
      ],
    });
    TestBed.overrideComponent(VisualSearchPage, { remove: { imports: [ProductCard] }, add: { imports: [CardStub] } });
    fixture = TestBed.createComponent(VisualSearchPage);
    await fixture.whenStable();
  });

  const page = () => fixture.nativeElement as HTMLElement;

  /** A nearest picture always exists, so the page never says it found the thing. */
  it('promises the nearest things, not that it found the one in the picture', async () => {
    matches.set([match('LAMP', 'img-lamp')]);
    await fixture.whenStable();

    expect(page().querySelector('h1')?.textContent).toContain('The nearest things we have');
    expect(page().textContent).toContain('We may not sell the thing itself.');
    expect(page().textContent).not.toMatch(/found/i);
  });

  it('draws each card in the answer’s order, with the picture that matched and not the product’s cover', async () => {
    matches.set([match('LAMP', 'img-lamp-brass'), match('STOOL', 'img-stool')]);
    await fixture.whenStable();

    const cards = [...page().querySelectorAll('storefront-product-card a')];
    expect(cards.map((card) => [card.textContent, card.getAttribute('data-image')])).toEqual([
      ['LAMP', 'img-lamp-brass'],
      ['STOOL', 'img-stool'],
    ]);
  });

  it('shows the shopper’s own picture beside the answer, from the page’s memory', async () => {
    matches.set([match('LAMP', 'img-lamp')]);
    preview.set('blob:mine');
    await fixture.whenStable();

    const picture = page().querySelector<HTMLImageElement>('header img')!;
    expect([picture.getAttribute('src'), picture.alt]).toEqual(['blob:mine', 'Your picture']);
  });

  it('counts a product followed by its place in the answer', async () => {
    matches.set([match('LAMP', 'a'), match('STOOL', 'b')]);
    await fixture.whenStable();

    page().querySelectorAll<HTMLElement>('storefront-product-card a')[1].click();

    expect(store.followed).toHaveBeenCalledExactlyOnceWith(2);
  });

  it('opens the panel again to try another picture, which is not a product followed', async () => {
    matches.set([match('LAMP', 'a')]);
    await fixture.whenStable();

    page().querySelector<HTMLElement>('[data-visual-search-again]')!.click();

    expect(store.open).toHaveBeenCalledTimes(1);
    expect(store.followed).not.toHaveBeenCalled();
  });

  it('says so when no picture in the shop is near', async () => {
    matches.set([]);
    await fixture.whenStable();

    expect(page().textContent).toContain('No picture in the shop looks like yours.');
    expect(page().querySelector('storefront-product-card')).toBeNull();
  });

  /** Reloaded, or opened from a link: the picture was only ever in the page's memory. */
  it('asks for a picture when it has none, and opens the panel to give one', async () => {
    expect(page().textContent).toContain('Give the shop a picture.');
    expect(page().querySelector('h1')).toBeNull();

    page().querySelector<HTMLElement>('button')!.click();

    expect(store.open).toHaveBeenCalledTimes(1);
  });
});
