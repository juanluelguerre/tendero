import { Component, inject, output, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProposalView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { DateTimePipe } from '@tendero/shared-ui';
import { ConfirmDialog } from '../../../../worklist/confirm-dialog/confirm-dialog';
import { WorklistPane } from '../../../../worklist/worklist-pane/worklist-pane';
import { ProposalsStore } from '../proposals.store';

/**
 * The open proposal beside the list: what it changes, now and proposed; the
 * model's reason; where it came from — model, prompt, who asked, the command and
 * its trace; and Approve, the view's one clay action, or Reject with a reason.
 * A decided proposal shows what became of it instead of the buttons.
 */
@Component({
  selector: 'backoffice-proposal-pane',
  imports: [TranslocoDirective, WorklistPane, ConfirmDialog, DateTimePipe],
  templateUrl: './proposal-pane.html',
  host: { class: 'contents' },
})
export class ProposalPane {
  protected readonly store = inject(ProposalsStore);
  protected readonly culture = inject(CultureStore);

  /** The person closed the pane. */
  readonly closed = output<void>();

  private readonly rejection = viewChild(ConfirmDialog);
  private rejecting: ProposalView | null = null;

  protected askToReject(proposal: ProposalView): void {
    this.rejecting = proposal;
    this.rejection()?.open();
  }

  protected rejected(reason: string | undefined): void {
    if (this.rejecting && reason) this.store.reject(this.rejecting, reason);
    this.rejecting = null;
  }
}
