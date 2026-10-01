import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ReturnView } from '@tendero/shared-api';
import { returnStatusTone, StatusPill } from '@tendero/shared-ui';

/** A return's status as the list and the pane both say it: the word, its colour only making it findable. No state. */
@Component({
  selector: 'backoffice-return-status-label',
  imports: [TranslocoDirective, StatusPill],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './return-status-label.html',
  host: { class: 'contents' },
})
export class ReturnStatusLabel {
  readonly request = input.required<ReturnView>();

  protected readonly tone = computed(() => returnStatusTone(this.request().status));
}
