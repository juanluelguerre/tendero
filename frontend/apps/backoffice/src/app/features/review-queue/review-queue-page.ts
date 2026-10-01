import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BackofficeUsage } from '../../usage';
import { landing, stepOf } from '../../worklist/list-keys';
import { ReviewList } from './review-list/review-list';
import { ReviewPane } from './review-pane/review-pane';
import { ReviewQueueStore } from './review-queue.store';

/**
 * The review queue: what is imported, or withdrawn, waits here in Draft, and
 * only publishing makes a product exist for a customer (ADR 0012).
 *
 * As R-3 drew the lists of work (R-4.3): Publish stays on the row, the likely
 * move, and a product opens beside the list with what publishing it would
 * publish — every culture as stored, the images, the attributes, the price —
 * because "check it and put it on sale" (R-0, B1) needs the checking. Importing
 * moved to the products screen: a developer's action was this screen's one clay
 * button whenever the queue was empty (R-1, B-F7).
 *
 * There is NO optimistic state: a row leaves when the server confirms, not
 * before. With the outbox in the middle a product takes a moment to reach the
 * index, and promising in the UI what is not true in search is worse than
 * waiting 200 ms.
 */
@Component({
  selector: 'backoffice-review-queue-page',
  imports: [ReviewList, ReviewPane],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './review-queue-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list and the pane have of their own is in their folders.
  host: { class: 'block', '(document:keydown)': 'key($event)' },
  providers: [ReviewQueueStore],
})
export class ReviewQueuePage {
  protected readonly store = inject(ReviewQueueStore);
  private readonly usage = inject(BackofficeUsage);

  /** j and k move down and up the queue, the pane following (the redesign's R-3, P5). */
  protected key(event: KeyboardEvent): void {
    const step = stepOf(event, !!document.querySelector('dialog[open]'));
    if (!step) return;
    const items = this.store.drafts();
    const index = landing(items.findIndex((item) => item.code === this.store.selectedCode()), items.length, step);
    if (index === null) return;
    event.preventDefault();
    this.usage.affordance('keys', step === 1 ? 'next' : 'previous');
    this.store.open(items[index]);
  }
}
