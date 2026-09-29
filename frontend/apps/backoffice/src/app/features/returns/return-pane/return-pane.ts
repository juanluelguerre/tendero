import { ChangeDetectionStrategy, Component, inject, output, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { formatPrice } from '@tendero/shared-util';
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
  styleUrl: './return-pane.css',
})
export class ReturnPane {
  protected readonly store = inject(ReturnsStore);
  private readonly culture = inject(CultureStore);
  private readonly rejectDialog = viewChild(ConfirmDialog);

  /** The person closed the pane. */
  readonly closed = output<void>();

  protected readonly nextDecision = nextDecision;
  protected readonly rejectable = rejectable;

  protected shortId(id: string): string {
    return id.split('-')[0];
  }

  protected money(amount: number): string {
    return formatPrice(amount, 'EUR', this.culture.active());
  }

  protected askToReject(): void {
    this.rejectDialog()?.open();
  }
}
