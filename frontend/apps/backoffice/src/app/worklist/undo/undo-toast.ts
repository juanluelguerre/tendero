import { Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { UndoStore } from './undo.store';
import { Icon } from '@tendero/shared-ui';

/** Where the offer shows: once, in the shell, announced politely. */
@Component({
  selector: 'backoffice-undo-toast',
  imports: [TranslocoDirective, Icon],
  templateUrl: './undo-toast.html',
  // The host's place: fixed at the foot of the viewport, centred, and
  // letting the pointer through everywhere but the toast.
  host: {
    class: 'fixed inset-x-0 bottom-5 z-5 flex justify-center pointer-events-none',
    role: 'status',
    'aria-live': 'polite',
  },
})
export class UndoToast {
  protected readonly undo = inject(UndoStore);
}
