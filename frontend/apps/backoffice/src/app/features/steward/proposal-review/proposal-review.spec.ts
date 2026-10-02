import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { ProposalView } from '@tendero/shared-api';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ProposalReview } from './proposal-review';

const Proposals = 'http://api/api/steward/proposals';

const proposal = (id: string) =>
  ({
    id,
    status: 'Proposed',
    subject: id.toUpperCase(),
    title: `Taza ${id}`,
    topic: 'English name',
    proposedAt: '2026-10-02T10:42:00Z',
    provenance: { model: 'qwen', promptVersion: 'steward-1', askedBy: 'Lucía' },
    changes: [{ field: 'Name · EN', now: null, proposed: `Mug ${id}` }],
  }) as unknown as ProposalView;

/** The review as a person meets it: every row ticked, the count in the foot, a reason asked before rejecting. */
describe('the proposal review', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    // jsdom has no modal dialogs: opening one is the platform's, not the review's.
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

  async function opened() {
    const fixture = TestBed.createComponent(ProposalReview);
    fixture.componentInstance.open(['a', 'b']);
    fixture.detectChanges();
    http.expectOne((req) => req.url === Proposals).flush({ items: [proposal('a'), proposal('b')], counts: [] });
    // The keys stand for the words: what is tested is what is shown and pressed.
    for (const translations of http.match((req) => req.url.includes('/i18n/'))) translations.flush({});
    await TestBed.inject(ApplicationRef).whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const buttons = (element: HTMLElement) => [...element.querySelectorAll('footer button')] as HTMLButtonElement[];

  it('shows every proposal ticked, with what it changes', async () => {
    const element = (await opened()).nativeElement as HTMLElement;

    const boxes = [...element.querySelectorAll('input[type=checkbox]')] as HTMLInputElement[];
    expect(boxes.map((box) => box.checked)).toEqual([true, true]);
    expect(element.querySelector('tbody')?.textContent).toContain('Mug a');
    const all = buttons(element);
    expect(all[all.length - 1].disabled).toBe(false);
  });

  it('asks why before rejecting the unticked one', async () => {
    const fixture = await opened();
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('input[type=checkbox]') as HTMLInputElement).click();
    fixture.detectChanges();

    const reject = buttons(element)[1];
    reject.click();
    fixture.detectChanges();

    expect([!!element.querySelector('textarea'), buttons(element)[1].disabled]).toEqual([true, true]);
    expect(http.match(`${Proposals}/reject`)).toHaveLength(0);
  });
});
