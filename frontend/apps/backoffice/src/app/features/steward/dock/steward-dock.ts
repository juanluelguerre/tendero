import { Component, inject } from '@angular/core';
import { translateSignal } from '@jsverse/transloco';
import { ModalSheet } from '@tendero/shared-ui';
import { SidePane } from '../../../shell/side-pane/side-pane';
import { StewardPanel } from '../panel/steward-panel';
import { StewardDoorStore } from '../steward-door.store';

/**
 * Where Steward's panel sits (spec, *The panel*): a column beside the content
 * from 85 rem, a drawer from the right below it and a full-screen sheet on a phone, both a modal `<dialog>` — so
 * the platform makes the page under it inert, closes it on Esc and gives the
 * focus back to whatever opened it, as the shell's own phone navigation does
 * (the review of S-4, I-3). The panel itself is deferred: nobody who never
 * opens Steward loads it.
 */
@Component({
  selector: 'backoffice-steward-dock',
  imports: [StewardPanel, ModalSheet],
  templateUrl: './steward-dock.html',
  // The column is the shell grid's third, as if it were the shell's own child.
  host: { class: 'contents' },
})
export class StewardDock {
  protected readonly door = inject(StewardDoorStore);

  /** The landmark's and the dialog's name. Not through the template's directive:
      the dialog must exist to be opened whether or not the words have loaded. */
  protected readonly name = translateSignal('steward.name');

  /** Whether the panes sit beside the page (from 85 rem) or are drawers over it. */
  protected readonly wide = inject(SidePane).stewardDocked;
}
