import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { UndoStore } from './undo.store';

/** Where the offer shows: once, in the shell, announced politely. */
@Component({
  selector: 'backoffice-undo-toast',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './undo-toast.html',
  styleUrl: './undo-toast.css',
  host: { role: 'status', 'aria-live': 'polite' },
})
export class UndoToast {
  protected readonly undo = inject(UndoStore);
}
