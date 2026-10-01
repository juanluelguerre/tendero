import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The three states every list of work passes through before it is a list —
 * loading, failed, empty — written once (ADR 0049, point 4). The list itself is
 * projected and shown only when none of them holds; the words are the screen's.
 *
 * Loading is said in a live region that is always there and changes its words,
 * rather than one inserted with its words already in it: `role="status"` is
 * already polite (and atomic), so the `aria-live` some lists added said nothing
 * new, but a region a screen reader first meets with its text inside is one it
 * may never read out. A failure is `role="alert"`, the one kind of region that
 * is read when it appears.
 */
@Component({
  selector: 'backoffice-list-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './list-state.html',
  styleUrl: './list-state.css',
})
export class ListState {
  readonly loading = input.required<boolean>();
  readonly failed = input(false);
  readonly empty = input(false);
  readonly loadingText = input.required<string>();
  readonly failedText = input.required<string>();
  readonly emptyText = input('');
}
