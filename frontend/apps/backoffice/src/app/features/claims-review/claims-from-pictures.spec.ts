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

const claim = (claimId: string, fromPicture: boolean, contradicts = false, sure = false) =>
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
    contradicts: contradicts ? { attributeCode: 'MATERIAL', claimed: 'CARBON_STEEL', held: 'acero inoxidable' } : null,
    // The server's rule, as it sends it: a picture's own answer the model was sure of is not decided alone.
    decidedAlone: contradicts ? 'ContradictsTheCatalogue' : fromPicture && !sure ? 'ReadFromAPicture' : null,
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
          items: [
            claim('written', false),
            claim('read', true),
            claim('against', false, true),
            claim('sure', true, false, true),
          ],
          total: 4,
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
    expect(written.textContent).not.toContain('claims.contradicts');

    expect(written.textContent).not.toContain('claims.fromPicture');
    expect(labels(written)).toContain('claims.approve');

    expect(read.textContent).toContain('claims.fromPicture');
    expect(labels(read)).not.toContain('claims.approve');
    // Rejecting needs no look at the picture: nothing reaches the catalogue.
    expect(labels(read)).toContain('claims.reject');
  });

  it('lets a sure answer from a picture be approved from its row, and says it is one', async () => {
    const page = await at('/?support=all');
    const sure = [...page.querySelectorAll('tbody tr')][3];

    expect(sure.textContent).toContain('claims.fromPicture');
    expect(sure.textContent).toContain('claims.sureAnswer');
    expect(labels(sure)).toContain('claims.approve');
    // And the one the model was not sure of does not say so.
    expect([...page.querySelectorAll('tbody tr')][1].textContent).not.toContain('claims.sureAnswer');
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

  /**
   * The catalogue holds another value for the attribute a claim types (I-4,
   * *Picture against data*, and for a claim from any source): both are named,
   * neither is corrected, and approving — which replaces the catalogue's — is
   * done with the two in front, from the pane.
   */
  it('names what the catalogue holds beside a claim that says otherwise, and approves only from the pane', async () => {
    const page = await at('/?support=all&claim=against');
    const row = [...page.querySelectorAll('tbody tr')][2];
    const pane = page.querySelector('backoffice-claim-pane') as HTMLElement;

    expect(row.textContent).toContain('claims.contradicts');
    expect(labels(row)).not.toContain('claims.approve');
    expect(labels(row)).toContain('claims.reject');

    const values = [...pane.querySelectorAll('dl dd')].map((value) => value.textContent?.trim());
    expect(values).toEqual(['acero inoxidable', 'CARBON_STEEL']);
    expect(pane.textContent).toContain('claims.contradictsNotice');
    expect(labels(pane)).toContain('claims.approve');
  });

  it('says nothing of the catalogue for a claim that agrees with it', async () => {
    const page = await at('/?support=all&claim=written');

    expect(page.querySelector('backoffice-claim-pane dl')).toBeNull();
  });
});
