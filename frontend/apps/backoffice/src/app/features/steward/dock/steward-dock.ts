import { Component, DestroyRef, type ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { translateSignal } from '@jsverse/transloco';
import { StewardPanel } from '../panel/steward-panel';
import { StewardDoorStore } from '../steward-door.store';

/** Where the panel stops being a sheet and sits beside the content: Tailwind's `lg`. */
const Wide = '(min-width: 64rem)';

/**
 * Where Steward's panel sits (spec, *The panel*): a column beside the content
 * from 1024 px, and below it a full-screen sheet that is a modal `<dialog>` — so
 * the platform makes the page under it inert, closes it on Esc and gives the
 * focus back to whatever opened it, as the shell's own phone navigation does
 * (the review of S-4, I-3). The panel itself is deferred: nobody who never
 * opens Steward loads it.
 */
@Component({
  selector: 'backoffice-steward-dock',
  imports: [StewardPanel],
  templateUrl: './steward-dock.html',
  // The column is the shell grid's third, as if it were the shell's own child.
  host: { class: 'contents' },
})
export class StewardDock {
  protected readonly door = inject(StewardDoorStore);

  /** The landmark's and the dialog's name. Not through the template's directive:
      the dialog must exist to be opened whether or not the words have loaded. */
  protected readonly name = translateSignal('steward.name');

  /** Whether the screen is wide enough for a column, followed as it changes. */
  protected readonly wide = signal(false);

  private readonly sheet = viewChild<ElementRef<HTMLDialogElement>>('sheet');

  constructor() {
    const query = window.matchMedia(Wide);
    this.wide.set(query.matches);
    const changed = (event: MediaQueryListEvent) => this.wide.set(event.matches);
    query.addEventListener('change', changed);
    inject(DestroyRef).onDestroy(() => query.removeEventListener('change', changed));

    // The dialog is the DOM's: opened when the sheet is asked for, closed with the door.
    effect(() => {
      const dialog = this.sheet()?.nativeElement;
      if (!dialog) return;
      if (this.door.isOpen() && !dialog.open) dialog.showModal();
      else if (!this.door.isOpen() && dialog.open) dialog.close();
    });
  }
}
