import { Component, type ElementRef, input, viewChild } from '@angular/core';

/**
 * The review a batch a model proposed goes through before anything changes
 * (Steward spec, decision 5: *one review component for everything a model
 * proposes*) — its frame: a native modal dialog, so the platform traps the
 * focus, closes on `Esc` and makes the page inert; a head, a table that scrolls,
 * and a foot that stays in view with the count and the actions. What the rows
 * are and what deciding does is its user's: Steward's proposals and a product's
 * claims each project their own head, table and foot into it.
 *
 * Slots: `[reviewHead]`, the table (unnamed), `[reviewNote]` above the actions,
 * `[reviewFoot]` the count and the actions.
 */
@Component({
  selector: 'backoffice-review-dialog',
  templateUrl: './review-dialog.html',
})
export class ReviewDialog {
  /** The id of the heading its user projects: the dialog's accessible name. */
  readonly labelledBy = input.required<string>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  open(): void {
    this.dialog().nativeElement.showModal();
  }

  close(): void {
    this.dialog().nativeElement.close();
  }
}
