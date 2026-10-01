import { Component, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ClaimForReview } from '@tendero/shared-api';
import { percent } from '../claim-statement';

/**
 * A claim's support, as the list and the pane both say it. Support is a
 * probability from the NLI scorer, never the model's word about itself
 * (ADR 0018); the weak ones are the rows to read first. No state.
 */
@Component({
  selector: 'backoffice-claim-support',
  imports: [TranslocoDirective],
  templateUrl: './claim-support.html',
  host: { class: 'contents' },
})
export class ClaimSupport {
  readonly claim = input.required<ClaimForReview>();
  protected readonly percent = percent;
}
