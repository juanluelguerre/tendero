import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTenderoI18n } from '@tendero/shared-i18n';
import { API_BASE_URL } from '@tendero/shared-util';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StewardDoorStore } from '../steward-door.store';
import { StewardDock } from './steward-dock';

/**
 * Where Steward's panel sits (spec, *The panel*): beside the content from 1024 px,
 * a sheet below — and the sheet is a modal dialog, so the page under it is inert,
 * Esc closes it and the focus goes back where it was (the review of S-4, I-3).
 */
describe('the steward dock', () => {
  function at(wide: boolean) {
    // jsdom has no media queries: the width is the test's to say.
    window.matchMedia = vi.fn().mockReturnValue({
      matches: wide,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    } as unknown as MediaQueryList);
  }

  beforeEach(() => {
    // Assigned, not `??=`: other specs in this worker stand in for the dialog with a
    // `close` that fires no event, and whichever ran first would be the one kept —
    // this spec is about that event, and failed one run in four without its own.
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.open = true;
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.open = false;
      this.dispatchEvent(new Event('close'));
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTenderoI18n(),
        { provide: API_BASE_URL, useValue: 'http://api' },
      ],
    });
  });

  afterEach(() => vi.restoreAllMocks());

  function opened(wide: boolean) {
    at(wide);
    const fixture = TestBed.createComponent(StewardDock);
    TestBed.inject(StewardDoorStore).open();
    fixture.detectChanges();
    TestBed.tick();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('is a modal sheet below 1024 px', () => {
    const dialog = opened(false).querySelector('dialog');

    expect(dialog?.open).toBe(true);
  });

  it('is a column beside the content from 1024 px, never a modal', () => {
    const element = opened(true);

    expect([element.querySelector('dialog'), !!element.querySelector('aside')]).toEqual([null, true]);
  });

  /** Esc, or anything that closes the dialog, closes Steward: the door and the sheet never disagree. */
  it('closes Steward when the sheet is closed', () => {
    const element = opened(false);
    element.querySelector('dialog')?.close();

    expect(TestBed.inject(StewardDoorStore).isOpen()).toBe(false);
  });
});
