import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ClaimForReview } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ClaimsBulkReview } from './claims-bulk-review';

const Claims = 'http://api/api/knowledge/claims';

const claim = (claimId: string, isWeak = false, fromPicture = false, contradicts = false) =>
  ({
    claimId,
    productId: 'p1',
    attributeCode: claimId,
    value: 'v',
    isWeak,
    picture: fromPicture ? { imageId: 'img', readBy: 'model', promptVersion: 'v1' } : null,
    contradicts: contradicts ? { attributeCode: claimId, claimed: 'v', held: 'otro' } : null,
    decidedAlone: isWeak
      ? 'WeakSupport'
      : contradicts
        ? 'ContradictsTheCatalogue'
        : fromPicture
          ? 'ReadFromAPicture'
          : null,
    statementSpanish: '',
    statementEnglish: '',
    quotes: [{ culture: 'es', text: `cita ${claimId}`, start: 0, end: 1, support: 0.5 }],
  }) as unknown as ClaimForReview;

/** The bulk review of claims as a person meets it: the weak one unticked, with the way to decide it alone. */
describe('the claims bulk review', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.open = true;
    };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) {
      this.open = false;
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.match(() => true));

  it('leaves the weak claim unticked and offers to open it on its own', async () => {
    const fixture = TestBed.createComponent(ClaimsBulkReview);
    const opened: { claimId: string; productId: string }[] = [];
    fixture.componentInstance.openClaim.subscribe((claim) => opened.push(claim));
    fixture.componentInstance.open('p1', 'Taza');
    fixture.detectChanges();
    http.expectOne((req) => req.url === Claims).flush({ items: [claim('a'), claim('b', true)], counts: [] });
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const boxes = [...element.querySelectorAll('input[type=checkbox]')] as HTMLInputElement[];
    expect(boxes.map((box) => [box.checked, box.disabled])).toEqual([
      [true, false],
      [false, true],
    ]);
    (element.querySelector('tbody button') as HTMLButtonElement).click();
    expect(opened).toEqual([{ claimId: 'b', productId: 'p1' }]);
  });

  it('leaves a claim read from a picture unticked and says why, in other words than a weak one', async () => {
    const fixture = TestBed.createComponent(ClaimsBulkReview);
    fixture.componentInstance.open('p1', 'Taza');
    fixture.detectChanges();
    http
      .expectOne((req) => req.url === Claims)
      .flush({
        items: [claim('a'), claim('p', false, true), claim('c', false, false, true), claim('w', true)],
        counts: [],
      });
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const boxes = [...element.querySelectorAll('input[type=checkbox]')] as HTMLInputElement[];
    expect(boxes.map((box) => [box.checked, box.disabled])).toEqual([
      [true, false],
      [false, true],
      [false, true],
      [false, true],
    ]);
    // No translations are loaded here, so a label is its key: each reason has its own.
    const reasons = [...element.querySelectorAll('tbody button')].map((button) => button.textContent?.trim());
    expect(reasons).toEqual([
      'claimsBulk.alone.ReadFromAPicture',
      'claimsBulk.alone.ContradictsTheCatalogue',
      'claimsBulk.alone.WeakSupport',
    ]);
  });
});
