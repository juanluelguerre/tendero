import { Component, type ElementRef, inject, output, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CultureStore } from '@tendero/shared-i18n';
import { DateTimePipe } from '@tendero/shared-ui';
import { ProposalReviewStore } from './proposal-review.store';

let reviews = 0;

/**
 * The review a batch of proposals goes through before anything changes (spec,
 * *The review*; decision 4): a native modal dialog — the platform traps the
 * focus, closes on `Esc` and makes the page inert — with every proposal as a
 * row, its provenance in the head, and in the foot how many are ticked, Cancel,
 * Reject the unticked (which asks why) and Approve, the view's one clay action.
 * After deciding, each row shows its own result in place.
 */
@Component({
  selector: 'backoffice-proposal-review',
  imports: [TranslocoDirective, DateTimePipe],
  templateUrl: './proposal-review.html',
  providers: [ProposalReviewStore],
})
export class ProposalReview {
  protected readonly store = inject(ProposalReviewStore);
  protected readonly culture = inject(CultureStore);

  /** Something was decided: the badge and the card that opened the review read again. */
  readonly decided = output<void>();

  protected readonly headingId = `review-${++reviews}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  /** The reason for rejecting the unticked: it lives while it is typed. */
  protected readonly asking = signal(false);
  protected readonly reason = signal('');

  open(ids: readonly string[]): void {
    this.asking.set(false);
    this.reason.set('');
    this.store.load(ids);
    this.dialog().nativeElement.showModal();
  }

  protected close(): void {
    this.dialog().nativeElement.close();
  }

  protected approve(): void {
    this.store.approve(() => this.decided.emit());
  }

  /** The first press asks why; the second, with a reason, rejects. */
  protected reject(): void {
    if (!this.asking()) {
      this.asking.set(true);
      return;
    }
    if (!this.reason().trim()) return;
    this.store.rejectUnticked(this.reason(), () => this.decided.emit());
    this.asking.set(false);
    this.reason.set('');
  }
}
