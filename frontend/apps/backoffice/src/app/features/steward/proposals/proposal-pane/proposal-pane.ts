import { NgOptimizedImage } from '@angular/common';
import { Component, computed, inject, linkedSignal, output, viewChild } from '@angular/core';
import { form, FormField } from '@angular/forms/signals';
import { TranslocoDirective } from '@jsverse/transloco';
import type { ProposalView } from '@tendero/shared-api';
import { CultureStore } from '@tendero/shared-i18n';
import { DateTimePipe } from '@tendero/shared-ui';
import { ProductImageUrls } from '@tendero/shared-util';
import { ConfirmDialog } from '../../../../worklist/confirm-dialog/confirm-dialog';
import { WorklistPane } from '../../../../worklist/worklist-pane/worklist-pane';
import { ProposalsStore } from '../proposals.store';

/** One of a proposal's lines a person may correct: which, what it is called, and the words. */
interface Draft {
  key: string;
  field: string;
  text: string;
}

/**
 * The open proposal beside the list: the picture it is about, when it is about
 * one; what it changes, now and proposed; the model's reason; where it came from —
 * who proposed, the model, the prompt, who asked, the command and its trace; and
 * Approve, the view's one clay action, or Reject with a reason. Where the proposal
 * offers its words to be corrected, they are in boxes, and approving sends the
 * corrections — the third decision. A decided proposal shows what became of it
 * instead of the buttons.
 */
@Component({
  selector: 'backoffice-proposal-pane',
  imports: [TranslocoDirective, WorklistPane, ConfirmDialog, DateTimePipe, NgOptimizedImage, FormField],
  templateUrl: './proposal-pane.html',
  host: { class: 'contents' },
})
export class ProposalPane {
  protected readonly store = inject(ProposalsStore);
  protected readonly culture = inject(CultureStore);
  protected readonly images = inject(ProductImageUrls);

  /** The person closed the pane. */
  readonly closed = output<void>();

  /**
   * The words being corrected: input that lives while it is typed, so a signal of
   * this component's and not the store's — written again from the proposal each
   * time another one opens (ADR 0051). A Signal Form over it binds the boxes.
   */
  protected readonly drafts = linkedSignal<Draft[]>(() => {
    const proposal = this.store.selected();
    return proposal?.status === 'Proposed'
      ? proposal.changes.flatMap((change) =>
          change.key ? [{ key: change.key, field: change.field, text: change.proposed ?? '' }] : [],
        )
      : [];
  });
  protected readonly draftsForm = form(this.drafts);

  /** Whether this proposal offers words a person may correct. */
  protected readonly correctable = computed(() => this.drafts().length > 0);

  /** The words that are no longer the model's, by their key; null when nothing was changed. */
  protected readonly corrections = computed<Record<string, string> | null>(() => {
    const proposed = new Map(
      (this.store.selected()?.changes ?? []).map((change) => [change.key, change.proposed ?? '']),
    );
    const changed = this.drafts().filter((draft) => draft.text !== proposed.get(draft.key));
    return changed.length > 0 ? Object.fromEntries(changed.map((draft) => [draft.key, draft.text])) : null;
  });

  private readonly rejection = viewChild(ConfirmDialog);
  private rejecting: ProposalView | null = null;

  /** Approved as proposed, or with the words the person corrected. */
  protected approve(proposal: ProposalView): void {
    const corrections = this.corrections();
    if (corrections) this.store.approveEdited(proposal, corrections);
    else this.store.approve(proposal);
  }

  protected askToReject(proposal: ProposalView): void {
    this.rejecting = proposal;
    this.rejection()?.open();
  }

  protected rejected(reason: string | undefined): void {
    if (this.rejecting && reason) this.store.reject(this.rejecting, reason);
    this.rejecting = null;
  }
}
