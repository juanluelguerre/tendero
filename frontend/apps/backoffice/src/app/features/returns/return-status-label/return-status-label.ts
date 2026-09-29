import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReturnView } from '@tendero/shared-api';

/** A return's status as the list and the pane both say it: the word, its colour only making it findable. No state. */
@Component({
  selector: 'backoffice-return-status-label',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './return-status-label.html',
  styleUrl: './return-status-label.css',
})
export class ReturnStatusLabel {
  readonly request = input.required<ReturnView>();
}
