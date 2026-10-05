import { Component, inject } from '@angular/core';
import { translateSignal } from '@jsverse/transloco';
import { ModalSheet } from '@tendero/shared-ui';
import { TinoDoorStore } from '../tino-door.store';
import { TinoPanel } from '../tino-panel/tino-panel';

/**
 * Where Tino's panel sits (the Tino spec, *The conversation*): beside the page
 * from 64 rem — the shell gives it the room, so the shop is still there to
 * browse — and a full-screen sheet below, a modal `<dialog>`: the platform
 * makes the page under it inert, closes it on Esc and gives the focus back to
 * the door. The panel itself is deferred: a shopper who never opens Tino
 * downloads nothing of it.
 */
@Component({
  selector: 'storefront-tino-dock',
  imports: [TinoPanel, ModalSheet],
  templateUrl: './tino-dock.html',
  host: { class: 'contents' },
})
export class TinoDock {
  protected readonly door = inject(TinoDoorStore);

  /** The landmark's and the dialog's name. Not through the template's directive:
      the dialog must exist to be opened whether or not the words have loaded. */
  protected readonly name = translateSignal('tino.name');
}
