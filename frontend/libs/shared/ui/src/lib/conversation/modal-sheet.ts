import { Directive, effect, ElementRef, inject, input } from '@angular/core';

/**
 * A `<dialog>` that is a modal sheet while its condition holds: the platform
 * makes the page under it inert, closes it on Esc and gives the focus back to
 * whatever opened it. The dialog is the DOM's, so an effect opens and closes it;
 * its own `(close)` event is the caller's to hear, so what opened it and the
 * sheet never disagree.
 */
@Directive({ selector: 'dialog[libModalSheet]' })
export class ModalSheet {
  readonly open = input.required<boolean>({ alias: 'libModalSheet' });

  constructor() {
    const dialog = inject<ElementRef<HTMLDialogElement>>(ElementRef).nativeElement;
    effect(() => {
      const open = this.open();
      if (open && !dialog.open) dialog.showModal();
      else if (!open && dialog.open) dialog.close();
    });
  }
}
