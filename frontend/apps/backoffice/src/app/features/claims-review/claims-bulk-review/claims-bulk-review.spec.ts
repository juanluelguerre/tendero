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

const claim = (claimId: string, isWeak = false) =>
  ({
    claimId,
    productId: 'p1',
    attributeCode: claimId,
    value: 'v',
    isWeak,
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
    const opened: string[] = [];
    fixture.componentInstance.openClaim.subscribe((id) => opened.push(id));
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
    expect(opened).toEqual(['b']);
  });
});
