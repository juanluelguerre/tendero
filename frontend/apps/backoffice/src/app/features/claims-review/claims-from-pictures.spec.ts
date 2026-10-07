import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import type { ClaimForReview } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ClaimList } from './claim-list/claim-list';
import { ClaimPane } from './claim-pane/claim-pane';
import { ClaimsReviewStore } from './claims-review.store';

const Claims = 'http://api/api/knowledge/claims';
const Products = 'http://api/api/catalog/products';

const claim = (claimId: string, fromPicture: boolean) =>
  ({
    claimId,
    productId: 'p1',
    statementSpanish: `Afirmación ${claimId}`,
    statementEnglish: `Claim ${claimId}`,
    attributeCode: null,
    value: null,
    support: 0.95,
    isWeak: false,
    quotes: [{ culture: 'es', text: 'base redonda', start: 0, end: 12, support: 0.95 }],
    extractor: 'chat',
    model: 'extractor-model',
    picture: fromPicture ? { imageId: 'lamp-front', readBy: 'vision-model', promptVersion: 'v1' } : null,
    decidedAlone: fromPicture ? 'ReadFromAPicture' : null,
  }) as unknown as ClaimForReview;

/** The list and the pane as the page holds them: one store, provided once. */
@Component({
  imports: [ClaimList, ClaimPane],
  providers: [ClaimsReviewStore],
  template: `<backoffice-claim-list /><backoffice-claim-pane />`,
})
class Queue {}

/**
 * A claim read from a picture, as a person meets it (phase 13a, I-4): what
 * supports it is a model's description, so the person is the only check that is
 * not a model — and has to see the picture to be one. The row says where it came
 * from and cannot approve it; the pane shows the picture, and approves.
 */
describe('claims read from a picture', () => {
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

  async function at(url: string) {
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(Queue);
    fixture.detectChanges();
    http
      .match((req) => req.url === Claims)
      .forEach((req) =>
        req.flush({
          items: [claim('written', false), claim('read', true)],
          total: 2,
          page: 1,
          pageSize: 20,
          counts: [],
        }),
      );
    http
      .match((req) => req.url === Products)
      .forEach((req) => req.flush({ items: [{ productId: 'p1', name: 'Flexo' }], total: 1 }));
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // No translations are loaded here, so a label is its key.
  const labels = (within: Element) =>
    [...within.querySelectorAll('button')].map((button) => button.textContent?.trim());

  it('marks the row, and leaves approving it to the pane where the picture is', async () => {
    const page = await at('/?support=all');
    const [written, read] = [...page.querySelectorAll('tbody tr')];

    expect(written.textContent).not.toContain('claims.fromPicture');
    expect(labels(written)).toContain('claims.approve');

    expect(read.textContent).toContain('claims.fromPicture');
    expect(labels(read)).not.toContain('claims.approve');
    // Rejecting needs no look at the picture: nothing reaches the catalogue.
    expect(labels(read)).toContain('claims.reject');
  });

  it('shows the picture the claim was read from, says whose words the quotes are, and approves there', async () => {
    const page = await at('/?support=all&claim=read');
    const pane = page.querySelector('backoffice-claim-pane') as HTMLElement;

    const picture = pane.querySelector('figure img') as HTMLImageElement;
    expect(picture.getAttribute('src') ?? picture.getAttribute('ng-reflect-ng-src') ?? picture.outerHTML).toContain(
      '/api/images/lamp-front',
    );
    expect(pane.textContent).toContain('claims.pictureNotice');
    expect(pane.textContent).toContain('claims.readBy');
    expect(labels(pane)).toContain('claims.approve');
  });

  it('shows no picture for a claim read from the description', async () => {
    const page = await at('/?support=all&claim=written');
    const pane = page.querySelector('backoffice-claim-pane') as HTMLElement;

    expect(pane.querySelector('figure')).toBeNull();
    expect(pane.textContent).not.toContain('claims.pictureNotice');
  });
});
