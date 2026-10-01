import { ChangeDetectionStrategy, Component, inject, output, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice, orderReference } from '@tendero/shared-util';
import { ConfirmDialog } from '../../../worklist/confirm-dialog/confirm-dialog';
import { WorklistPane } from '../../../worklist/worklist-pane/worklist-pane';
import { nextDecision, rejectable } from '../return-decisions';
import { ReturnStatusLabel } from '../return-status-label/return-status-label';
import { ReturnsStore } from '../returns.store';

/**
 * The open return beside the list: what comes back and why, in the customer's
 * words when they gave any, the refund, the view's one clay decision, and
 * rejecting apart at its foot, asking first.
 */
@Component({
  selector: 'backoffice-return-pane',
  imports: [TranslocoDirective, RouterLink, WorklistPane, ConfirmDialog, ReturnStatusLabel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './return-pane.html',
  // The pane is laid out by the page's list-and-pane grid, as if it were its child.
  host: { class: 'contents' },
})
export class ReturnPane {
  protected readonly store = inject(ReturnsStore);
  private readonly culture = inject(CultureStore);
  private readonly rejectDialog = viewChild(ConfirmDialog);

  /** The person closed the pane. */
  readonly closed = output<void>();

  protected readonly nextDecision = nextDecision;
  protected readonly rejectable = rejectable;

  /** A full Guid is unreadable; its random end is what a person reads, and what finds it. */
  protected readonly shortId = orderReference;

  protected money(amount: number): string {
    return formatPrice(amount, 'EUR', this.culture.active());
  }

  protected askToReject(): void {
    this.rejectDialog()?.open();
  }
}
