import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ReturnList } from './return-list/return-list';
import { ReturnPane } from './return-pane/return-pane';
import { ReturnsStore } from './returns.store';

/**
 * The returns queue, as R-3 drew the lists of work (R-4.3): it opens on what
 * waits for a decision, keeps the next step on the row, and shows a return
 * beside the list with its lines, their reasons and the customer's words.
 *
 * The column worth the room is **what is coming back and why**: the reason is a
 * closed set of five, the input to the return-reason analysis a later phase
 * promises. The two-step shape is the point of the screen. Approving is a
 * promise; receiving is a fact, and **only receiving restocks** — a shop that
 * put goods back when it said yes would be selling parcels still in the post.
 *
 * Rejecting cannot be undone, so it asks first and can say why; the next step
 * forward is never it.
 */
@Component({
  selector: 'backoffice-returns',
  imports: [ReturnList, ReturnPane],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './returns-page.html',
  // The page is a frame: the list-and-pane layout is shared by every worklist
  // (styles.css), and what the list and the pane have of their own is in their folders.
  host: { class: 'block' },
  providers: [ReturnsStore],
})
export class ReturnsPage {
  protected readonly store = inject(ReturnsStore);
}
