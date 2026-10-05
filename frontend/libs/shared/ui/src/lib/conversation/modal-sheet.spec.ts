import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { ModalSheet } from './modal-sheet';

@Component({
  imports: [ModalSheet],
  template: `<dialog [libModalSheet]="open()" (close)="open.set(false)"></dialog>`,
})
class Host {
  readonly open = signal(false);
}

/** A dialog that is modal while its condition holds: the docks' sheets (phase 12, 12b). */
describe('the modal sheet', () => {
  let modal = 0;

  beforeEach(() => {
    modal = 0;
    // jsdom's dialog does not open modally: what matters here is that it was asked to.
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      modal++;
      this.open = true;
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.open = false;
      this.dispatchEvent(new Event('close'));
    };
  });

  it('opens modally when its condition holds and closes when it does not', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const dialog = (fixture.nativeElement as HTMLElement).querySelector('dialog')!;
    expect(dialog.open).toBe(false);

    fixture.componentInstance.open.set(true);
    fixture.detectChanges();
    expect([dialog.open, modal]).toEqual([true, 1]);

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    expect(dialog.open).toBe(false);
  });

  /** Esc closes the dialog itself: the caller hears it, and the two do not disagree. */
  it('tells its caller when the platform closed it', () => {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement).querySelector('dialog')!.close();

    expect(fixture.componentInstance.open()).toBe(false);
  });
});
