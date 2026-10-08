import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { PictureDoubtView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { PictureDoubtsPage } from './picture-doubts-page';

const Doubts = 'http://api/api/knowledge/picture-doubts';
const Products = 'http://api/api/catalog/products';

const doubt = (doubtId: string, seenOnAnotherVariant = false): PictureDoubtView => ({
  doubtId,
  productId: 'p1',
  imageId: 'sleeve-front',
  attributeCode: 'COLOR',
  seen: 'GREY',
  seenLabel: 'gris',
  held: 'BLACK',
  heldLabel: 'negro',
  confidence: 0.94,
  readBy: 'vision-model',
  raisedAt: '2026-10-08T06:00:00Z',
  seenOnAnotherVariant,
});

/**
 * A doubt about a picture, as a person meets it (phase 13a, I-4d): the picture
 * itself, what the model saw and what the catalogue holds, and three things to
 * say of it — none of which is the one the screen leads to.
 */
describe('picture doubts', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  async function settle(items: PictureDoubtView[]) {
    http
      .match((req) => req.url === Doubts)
      .forEach((req) => req.flush({ items, total: items.length, page: 1, pageSize: 20 }));
    http
      .match((req) => req.url === Products)
      .forEach((req) => req.flush({ items: [{ productId: 'p1', name: 'Funda de portátil' }], total: 1 }));
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
  }

  async function page(items: PictureDoubtView[]) {
    await TestBed.inject(Router).navigateByUrl('/');
    const fixture = TestBed.createComponent(PictureDoubtsPage);
    fixture.detectChanges();
    await settle(items);
    fixture.detectChanges();
    return fixture;
  }

  // No translations are loaded here, so a label is its key.
  const labels = (within: Element) =>
    [...within.querySelectorAll('button')].map((button) => button.textContent?.trim());

  it('shows the picture beside the product, and the three things a person can say of it', async () => {
    const fixture = await page([doubt('d1')]);
    const row = (fixture.nativeElement as HTMLElement).querySelector('li')!;

    expect(row.textContent).toContain('Funda de portátil');
    expect(row.textContent).toContain('doubts.seen');
    const picture = row.querySelector('img') as HTMLImageElement;
    expect(picture.getAttribute('src') ?? picture.getAttribute('ng-reflect-ng-src') ?? picture.outerHTML).toContain(
      'sleeve-front',
    );
    expect(labels(row)).toEqual([
      'doubts.decision.PictureIsFine',
      'doubts.decision.ReplaceThePicture',
      'doubts.decision.BelongsToAnotherVariant',
    ]);
    // One clay action per view, and here none: no decision is the one to be led to.
    expect(row.querySelectorAll('button:not(.button--quiet)').length).toBe(0);
  });

  it("says so when the colour seen is another variant's of the same product, and only then", async () => {
    const fixture = await page([doubt('d1', true), doubt('d2')]);
    const [another, alone] = [...(fixture.nativeElement as HTMLElement).querySelectorAll('li')];

    expect(another.textContent).toContain('doubts.anotherVariant');
    expect(alone.textContent).not.toContain('doubts.anotherVariant');
  });

  it('sends the decision a person made, and reads the queue again', async () => {
    const fixture = await page([doubt('d1')]);
    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')];

    buttons[2].click();
    const sent = http.expectOne((req) => req.url === `${Doubts}/d1/decide`);
    expect(sent.request.body).toEqual({ decision: 'BelongsToAnotherVariant' });
    sent.flush({ doubtId: 'd1', decision: 'BelongsToAnotherVariant' });

    // The queue is the server's: it is asked again.
    TestBed.tick();
    expect(http.match((req) => req.url === Doubts).length).toBe(1);
  });

  it('reads the queue again when the server says the doubt no longer waits', async () => {
    const fixture = await page([doubt('d1')]);

    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
    http
      .expectOne((req) => req.url === `${Doubts}/d1/decide`)
      .flush({ title: 'no longer waiting' }, { status: 409, statusText: 'Conflict' });

    // The queue is the server's: it is asked again.
    TestBed.tick();
    expect(http.match((req) => req.url === Doubts).length).toBe(1);
  });

  it('says so when no picture is in doubt', async () => {
    const fixture = await page([]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('doubts.empty');
  });
});
